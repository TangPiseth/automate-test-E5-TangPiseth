using Moq;
using SmartPark.Core.Interfaces;
using SmartPark.Core.Models;
using SmartPark.Core.Services;

namespace SmartPark.Tests;

public class ParkingSessionManagerTests
{
    private readonly Mock<IPaymentGateway> _paymentStub = new();
    private readonly Mock<INotificationService> _notificationStub = new();
    private readonly Mock<IMembershipService> _membershipStub = new();
    private readonly Mock<IParkingRepository> _repoStub = new();
    private readonly Mock<IDateTimeProvider> _dateTimeStub = new();
    private readonly ParkingFeeCalculator _feeCalculator = new();
    private readonly ParkingSessionManager _manager;

    public ParkingSessionManagerTests()
    {
        _manager = new ParkingSessionManager(
            _feeCalculator,
            _paymentStub.Object,
            _notificationStub.Object,
            _membershipStub.Object,
            _repoStub.Object,
            _dateTimeStub.Object);
    }

    // Example test (kept but not counted in our minimum)
    [Fact]
    public async Task CheckInAsync_NewVehicle_LookUpMembership()
    {
        _membershipStub.Setup(m => m.GetMembershipTier("PP-9999")).Returns(MembershipTier.Guest);
        _repoStub.Setup(r => r.GetActiveTicketByPlateAsync("PP-9999")).ReturnsAsync((ParkingTicket?)null);
        _dateTimeStub.Setup(d => d.Now).Returns(new DateTime(2026, 3, 16, 10, 0, 0));

        var ticket = await _manager.CheckInAsync("PP-9999", VehicleType.Car);

        _membershipStub.Verify(m => m.GetMembershipTier("PP-9999"), Times.Once);
        Assert.Equal("PP-9999", ticket.Vehicle.LicensePlate);
    }

    #region Added test double scenarios (5 total)

    [Fact]
    public async Task CheckInAsync_Success_SavesTicket()
    {
        _membershipStub.Setup(m => m.GetMembershipTier("PP-123")).Returns(MembershipTier.Guest);
        _repoStub.Setup(r => r.GetActiveTicketByPlateAsync("PP-123")).ReturnsAsync((ParkingTicket?)null);
        _dateTimeStub.Setup(d => d.Now).Returns(new DateTime(2026, 3, 16, 10, 0, 0));

        var ticket = await _manager.CheckInAsync("PP-123", VehicleType.Car);

        _repoStub.Verify(r => r.SaveTicketAsync(It.IsAny<ParkingTicket>()), Times.Once);
        Assert.False(ticket.CheckOutTime.HasValue); // still active
    }

    [Fact]
    public async Task CheckInAsync_Duplicate_ThrowsInvalidOperation()
    {
        var existing = new ParkingTicket { Vehicle = new Vehicle { LicensePlate = "PP-123" } };
        _repoStub.Setup(r => r.GetActiveTicketByPlateAsync("PP-123")).ReturnsAsync(existing);

        await Assert.ThrowsAsync<InvalidOperationException>(() => _manager.CheckInAsync("PP-123", VehicleType.Car));
        _repoStub.Verify(r => r.SaveTicketAsync(It.IsAny<ParkingTicket>()), Times.Never);
    }

    [Fact]
    public async Task CheckOutAsync_Success_ProcessesPaymentAndSendsReceipt()
    {
        var ticket = new ParkingTicket
        {
            Vehicle = new Vehicle { LicensePlate = "PP-123", Type = VehicleType.Car, Membership = MembershipTier.Guest },
            CheckInTime = new DateTime(2026, 3, 16, 9, 0, 0)
        };
        _repoStub.Setup(r => r.GetTicketByIdAsync(ticket.TicketId)).ReturnsAsync(ticket);
        _paymentStub.Setup(p => p.ProcessPaymentAsync(ticket.TicketId, It.IsAny<decimal>())).ReturnsAsync(true);
        _dateTimeStub.Setup(d => d.Now).Returns(new DateTime(2026, 3, 16, 11, 0, 0));

        var result = await _manager.CheckOutAsync(ticket.TicketId, "012345678");

        _paymentStub.Verify(p => p.ProcessPaymentAsync(ticket.TicketId, It.Is<decimal>(f => f == 2_000)), Times.Once);
        _notificationStub.Verify(n => n.SendReceiptAsync("012345678", It.IsAny<string>()), Times.Once);
        Assert.Equal(2_000m, result.TotalFee);
    }

    [Fact]
    public async Task CheckOutAsync_PaymentFails_ThrowsAndDoesNotUpdate()
    {
        var ticket = new ParkingTicket
        {
            Vehicle = new Vehicle { LicensePlate = "PP-123", Type = VehicleType.Car, Membership = MembershipTier.Guest },
            CheckInTime = new DateTime(2026, 3, 16, 9, 0, 0)
        };
        _repoStub.Setup(r => r.GetTicketByIdAsync(ticket.TicketId)).ReturnsAsync(ticket);
        _paymentStub.Setup(p => p.ProcessPaymentAsync(It.IsAny<string>(), It.IsAny<decimal>())).ReturnsAsync(false);

        await Assert.ThrowsAsync<Exception>(() => _manager.CheckOutAsync(ticket.TicketId, "012345678"));
        _repoStub.Verify(r => r.UpdateTicketAsync(It.IsAny<ParkingTicket>()), Times.Never);
        _notificationStub.Verify(n => n.SendReceiptAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task CheckOutAsync_NotificationFails_StillSucceeds()
    {
        var ticket = new ParkingTicket
        {
            Vehicle = new Vehicle { LicensePlate = "PP-123", Type = VehicleType.Car, Membership = MembershipTier.Guest },
            CheckInTime = new DateTime(2026, 3, 16, 9, 0, 0)
        };
        _repoStub.Setup(r => r.GetTicketByIdAsync(ticket.TicketId)).ReturnsAsync(ticket);
        _paymentStub.Setup(p => p.ProcessPaymentAsync(It.IsAny<string>(), It.IsAny<decimal>())).ReturnsAsync(true);
        _notificationStub.Setup(n => n.SendReceiptAsync(It.IsAny<string>(), It.IsAny<string>()))
                         .ThrowsAsync(new Exception("SMS error"));

        var result = await _manager.CheckOutAsync(ticket.TicketId, "012345678");

        _repoStub.Verify(r => r.UpdateTicketAsync(It.IsAny<ParkingTicket>()), Times.Once);
        Assert.NotNull(result);
    }

    #endregion
}