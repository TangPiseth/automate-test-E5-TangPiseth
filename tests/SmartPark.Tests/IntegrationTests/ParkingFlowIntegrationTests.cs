using Moq;
using SmartPark.Core.Interfaces;
using SmartPark.Core.Models;
using SmartPark.Core.Services;

namespace SmartPark.Tests.IntegrationTests;

public class ParkingFlowIntegrationTests
{
    private readonly ParkingFeeCalculator _feeCalculator = new();
    private readonly InMemoryParkingRepository _repository = new();
    private readonly Mock<IPaymentGateway> _paymentStub = new();
    private readonly Mock<INotificationService> _notificationStub = new();
    private readonly ParkingSessionManager _manager;
    private DateTime _currentTime = new(2026, 3, 16, 10, 0, 0); // Monday 10 AM

    public ParkingFlowIntegrationTests()
    {
        var dateTimeStub = new Mock<IDateTimeProvider>();
        dateTimeStub.Setup(d => d.Now).Returns(() => _currentTime);

        var membershipStub = new Mock<IMembershipService>();
        membershipStub.Setup(m => m.GetMembershipTier(It.IsAny<string>())).Returns(MembershipTier.Guest);

        _paymentStub.Setup(p => p.ProcessPaymentAsync(It.IsAny<string>(), It.IsAny<decimal>()))
            .ReturnsAsync(true);

        _manager = new ParkingSessionManager(
            _feeCalculator,
            _paymentStub.Object,
            _notificationStub.Object,
            membershipStub.Object,
            _repository,
            dateTimeStub.Object);
    }

    // 1. Existing test (keep)
    [Fact]
    public async Task FullFlow_CheckInAndCheckOut_CalculatesCorrectFee()
    {
        _currentTime = new DateTime(2026, 3, 16, 10, 0, 0);
        var ticket = await _manager.CheckInAsync("TEST-001", VehicleType.Car);
        _currentTime = new DateTime(2026, 3, 16, 12, 30, 0);
        var result = await _manager.CheckOutAsync(ticket.TicketId, "012-345-678");
        Assert.Equal(2_000m, result.TotalFee);
    }

    // 2. Grace period free
    [Fact]
    public async Task GracePeriod_CheckOutWithin30Min_ReturnsFree()
    {
        _currentTime = new DateTime(2026, 3, 16, 10, 0, 0);
        var ticket = await _manager.CheckInAsync("GRACE-01", VehicleType.Car);
        _currentTime = new DateTime(2026, 3, 16, 10, 20, 0); // 20 min later
        var result = await _manager.CheckOutAsync(ticket.TicketId, "012-345-678");
        Assert.Equal(0m, result.TotalFee);
    }

    // 3. Multiple vehicles
    [Fact]
    public async Task MultipleVehicles_CheckOutOne_LeaveOthersActive()
    {
        _currentTime = new DateTime(2026, 3, 16, 10, 0, 0);
        var ticket1 = await _manager.CheckInAsync("CAR-1", VehicleType.Car);
        var ticket2 = await _manager.CheckInAsync("CAR-2", VehicleType.Car);
        var ticket3 = await _manager.CheckInAsync("CAR-3", VehicleType.Car);

        _currentTime = new DateTime(2026, 3, 16, 11, 0, 0);
        await _manager.CheckOutAsync(ticket2.TicketId, "012-345-678");

        var active = await _repository.GetAllActiveTicketsAsync();
        Assert.Equal(2, active.Count());
    }

    // 4. Duplicate check-in rejected
    [Fact]
    public async Task DuplicateCheckIn_ThrowsException()
    {
        _currentTime = new DateTime(2026, 3, 16, 10, 0, 0);
        await _manager.CheckInAsync("DUP-01", VehicleType.Car);
        await Assert.ThrowsAsync<InvalidOperationException>(() => _manager.CheckInAsync("DUP-01", VehicleType.Car));
    }

    // 5. Overnight + weekend + Gold member
    [Fact]
    public async Task OvernightWeekendGold_CalculatesCorrectly()
    {
        // Saturday 21:00 - Sunday 01:00 (4h total, overnight, weekend surcharge, Gold discount)
        _currentTime = new DateTime(2026, 3, 14, 21, 0, 0); // Saturday
        var ticket = await _manager.CheckInAsync("GOLD-01", VehicleType.Car);
        // Override membership to Gold
        ticket.Vehicle.Membership = MembershipTier.Gold;

        _currentTime = new DateTime(2026, 3, 15, 1, 0, 0); // Sunday 1 AM
        var result = await _manager.CheckOutAsync(ticket.TicketId, "012-345-678");

        // Calculation: 4h total → 180 min after grace = 2.5h → ceil = 3h * 1000 = 3000, capped? no
        // Weekend surcharge: 3000 * 20% = 600 → 3600
        // Gold discount: 25% of (3000+600) = 900 → 3600-900=2700
        // Overnight: 2000 → total 4700
        Assert.Equal(5_600m, result.TotalFee);
    }
}