using SmartPark.Core.Models;


namespace SmartPark.Core.Services;

/// <summary>
/// Core pricing engine. Pure calculation service with no external dependencies.
/// Students: implement this class using TDD (Red-Green-Refactor).
/// </summary>
public class ParkingFeeCalculator
{
    // ── Pricing constants (from spec §4) ────────────────────────

    // Hourly rates (KHR)
    private const decimal MotorcycleRatePerHour = 500m;
    private const decimal CarRatePerHour = 1_000m;
    private const decimal SuvRatePerHour = 1_500m;

    // Daily caps (KHR)
    private const decimal MotorcycleDailyCap = 4_000m;
    private const decimal CarDailyCap = 8_000m;
    private const decimal SuvDailyCap = 12_000m;

    // Time-based rules
    private const int GracePeriodMinutes = 30;
    private const decimal OvernightFlatFee = 2_000m;
    private const int OvernightHourThreshold = 22; // 10 PM

    // Surcharges
    private const decimal WeekendSurchargeRate = 0.20m;
    private const decimal HolidaySurchargeRate = 0.50m;

    // Membership discounts
    private const decimal SilverDiscountRate = 0.10m;
    private const decimal GoldDiscountRate = 0.25m;
    private const decimal PlatinumDiscountRate = 0.40m;

    // Penalties
    private const decimal LostTicketPenalty = 20_000m;

    /// <summary>
    /// Calculates the parking fee following the 9-step flow in the spec.
    /// </summary>
    /// <remarks>
    /// Steps:
    ///   1. Validate: checkOut before checkIn → ArgumentException
    ///   2. Grace period: total ≤ 30 min → free (lost-ticket penalty still applies)
    ///   3. Duration: billableHours = ⌈(totalMinutes − 30) / 60⌉, min 1
    ///   4. Base fee: billableHours × hourlyRate, capped at dailyCap
    ///   5. Overnight: +2,000 KHR if session spans past 22:00
    ///   6. Surcharge: weekend +20% OR holiday +50% on baseFee (not both)
    ///   7. Discount: (baseFee + surcharge) × membershipRate
    ///   8. Lost ticket: +20,000 KHR (not subject to discounts)
    ///   9. Total: baseFee + surcharge − discount + overnight + penalty (min 0)
    /// </remarks>
    public ParkingFeeResult CalculateFee(
    VehicleType vehicleType, MembershipTier membership,
    DateTime checkIn, DateTime checkOut,
    bool isLostTicket = false, bool isHoliday = false)
    {
        // 1. Validation
        if (checkOut < checkIn)
            throw new ArgumentException("Check-out cannot be before check-in.");

        var duration = checkOut - checkIn;
        int totalMinutes = (int)duration.TotalMinutes;

        // 2. Grace period – 0..30 min = free (lost ticket penalty still applies later)
        if (totalMinutes <= 30)
            return new ParkingFeeResult { TotalFee = 0m };

        // 3. Billable hours = ceil((totalMinutes - 30) / 60), minimum 1
        int billableMinutes = totalMinutes - 30;
        int billableHours = (int)Math.Ceiling(billableMinutes / 60.0);
        if (billableHours < 1) billableHours = 1;

        // 4. Base fee
        decimal hourlyRate = vehicleType switch
        {
            VehicleType.Motorcycle => MotorcycleRatePerHour,
            VehicleType.Car => CarRatePerHour,
            VehicleType.SUV => SuvRatePerHour,
            _ => throw new ArgumentException("Unknown vehicle type")
        };
        decimal baseFee = billableHours * hourlyRate;

        // Daily cap
        decimal dailyCap = vehicleType switch
        {
            VehicleType.Motorcycle => MotorcycleDailyCap,
            VehicleType.Car => CarDailyCap,
            VehicleType.SUV => SuvDailyCap,
            _ => throw new ArgumentException("Unknown vehicle type")
        };
        if (baseFee > dailyCap) baseFee = dailyCap;

        // 6. Surcharge (weekend / holiday) – holiday takes priority
        decimal surcharge = 0m;
        bool isWeekend = checkIn.DayOfWeek == DayOfWeek.Saturday
                         || checkIn.DayOfWeek == DayOfWeek.Sunday;
        if (isHoliday)
            surcharge = baseFee * HolidaySurchargeRate;
        else if (isWeekend)
            surcharge = baseFee * WeekendSurchargeRate;

        // 7. Membership discount (applies to baseFee + surcharge)
        decimal discountRate = membership switch
        {
            MembershipTier.Silver => SilverDiscountRate,
            MembershipTier.Gold => GoldDiscountRate,
            MembershipTier.Platinum => PlatinumDiscountRate,
            _ => 0m
        };
        decimal discount = (baseFee + surcharge) * discountRate;

        // 5. Overnight fee (if session goes past 22:00 or spans midnight)
        decimal overnightFee = 0m;
        if (checkOut.TimeOfDay >= new TimeSpan(OvernightHourThreshold, 0, 0)
            || checkOut.Date > checkIn.Date)
        {
            overnightFee = OvernightFlatFee;
        }

        // 8. Lost ticket penalty (not subject to discounts)
        decimal lostTicketFee = isLostTicket ? LostTicketPenalty : 0m;

        // 9. Total
        decimal total = baseFee + surcharge - discount + overnightFee + lostTicketFee;
        if (total < 0) total = 0;

        return new ParkingFeeResult { TotalFee = total };
    }
}
