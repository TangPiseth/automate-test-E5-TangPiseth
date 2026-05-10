using SmartPark.Core.Models;
using SmartPark.Core.Services;
using FsCheck;
using FsCheck.Xunit;

namespace SmartPark.Tests;

public class ParkingFeeCalculatorTests
{
    private readonly ParkingFeeCalculator _calculator = new();

    // ────────────────────────────────────────────────────────────
    //  EXAMPLE TEST — shows the naming convention and AAA pattern.
    //  Delete or keep this; it does not count toward your grade.
    // ────────────────────────────────────────────────────────────

    [Fact]
    public void CalculateFee_ZeroDuration_ReturnsFree()
    {
        // Arrange
        var checkIn = new DateTime(2026, 3, 16, 10, 0, 0);  // Monday
        var checkOut = checkIn; // same time = 0 duration

        // Act
        var result = _calculator.CalculateFee(VehicleType.Car, MembershipTier.Guest, checkIn, checkOut);

        // Assert
        Assert.Equal(0m, result.TotalFee);
    }

    #region Basic Fee Calculation
    [Theory]
    [InlineData(VehicleType.Car, 3, 3_000)]
    [InlineData(VehicleType.SUV, 1, 1_500)]
    public void CalculateFee_BasicRate_ReturnsCorrectFee(VehicleType type, int hours, decimal expected)
    {
        var checkIn = new DateTime(2026, 3, 16, 10, 0, 0);
        var checkOut = checkIn.AddHours(hours);

        var result = _calculator.CalculateFee(type, MembershipTier.Guest, checkIn, checkOut);

        Assert.Equal(expected, result.TotalFee);
    }
    #endregion

    #region Grace Period
    // Test the free parking window and its boundaries
    #endregion

    #region Duration Rounding
    // Test how partial hours are rounded for billing
    #endregion

    #region Daily Cap
    [Fact]
    public void CalculateFee_Motorcycle_10Hours_CappedAt4000()
    {
        var checkIn = new DateTime(2026, 3, 16, 8, 0, 0);
        var checkOut = checkIn.AddHours(10);

        var result = _calculator.CalculateFee(VehicleType.Motorcycle, MembershipTier.Guest, checkIn, checkOut);

        Assert.Equal(4_000m, result.TotalFee); // capped, not 5000
    }
    #endregion

    #region Overnight Fee
    [Fact]
    public void CalculateFee_Overnight_Adds2000()
    {
        // Check-in 8 PM, check-out 11 PM (crosses 10 PM)
        var checkIn = new DateTime(2026, 3, 16, 20, 0, 0);
        var checkOut = new DateTime(2026, 3, 16, 23, 0, 0);

        var result = _calculator.CalculateFee(VehicleType.Car, MembershipTier.Guest, checkIn, checkOut);

        // Duration: 3 hours total, minus 30 min grace = 2.5h → ceil = 3 billable hours? Wait let's compute correctly:
        // 3h = 180 min, minus grace 30 = 150 min → ceil(150/60) = 3 hours → 3000 base, +2000 overnight = 5000
        Assert.Equal(5_000m, result.TotalFee);
    }
    #endregion

    #region Weekend Surcharge
    [Fact]
    public void CalculateFee_Weekend_Saturday_Adds20Percent()
    {
        // Saturday
        var checkIn = new DateTime(2026, 3, 14, 10, 0, 0); // Saturday
        var checkOut = checkIn.AddHours(2);

        var result = _calculator.CalculateFee(VehicleType.Car, MembershipTier.Guest, checkIn, checkOut);

        // base 2000, surcharge 20% = 400 → 2400
        Assert.Equal(2_400m, result.TotalFee);
    }
    #endregion

    #region Holiday Surcharge
    [Fact]
    public void CalculateFee_Holiday_Adds50Percent()
    {
        var checkIn = new DateTime(2026, 3, 16, 10, 0, 0); // Monday
        var checkOut = checkIn.AddHours(2);
        var result = _calculator.CalculateFee(VehicleType.Car, MembershipTier.Guest, checkIn, checkOut, isHoliday: true);
        Assert.Equal(3_000m, result.TotalFee); // 2000 + 1000
    }
    #endregion

    #region Membership Discounts
    [Theory]
    [InlineData(MembershipTier.Silver, 1_800)]
    [InlineData(MembershipTier.Gold, 1_500)]
    [InlineData(MembershipTier.Platinum, 1_200)]
    public void CalculateFee_Membership_DiscountApplied(MembershipTier tier, decimal expected)
    {
        var checkIn = new DateTime(2026, 3, 16, 10, 0, 0);
        var checkOut = checkIn.AddHours(2);
        var result = _calculator.CalculateFee(VehicleType.Car, tier, checkIn, checkOut);
        Assert.Equal(expected, result.TotalFee);
    }
    #endregion

    #region Lost Ticket
    // Test the penalty and how it interacts with other fee modifiers
    #endregion

    #region Edge Cases
    [Fact]
    public void CalculateFee_CheckOutBeforeCheckIn_ThrowsArgumentException()
    {
        var checkIn = new DateTime(2026, 3, 16, 12, 0, 0);
        var checkOut = checkIn.AddHours(-1);  // before checkin

        Assert.Throws<ArgumentException>(() =>
            _calculator.CalculateFee(VehicleType.Car, MembershipTier.Guest, checkIn, checkOut));
    }
    #endregion

    #region Property-Based Tests
    // Write at least 5 FsCheck properties that must hold for ALL valid inputs
    // You may need custom Arbitrary<T> for generating valid DateTime pairs
    #endregion
}
