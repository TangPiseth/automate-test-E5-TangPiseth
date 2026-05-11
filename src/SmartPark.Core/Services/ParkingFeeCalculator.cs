using SmartPark.Core.Models;

namespace SmartPark.Core.Services;

public class ParkingFeeCalculator
{
    private const decimal MotorcycleRatePerHour = 500m;
    private const decimal CarRatePerHour = 1_000m;
    private const decimal SuvRatePerHour = 1_500m;

    private const decimal MotorcycleDailyCap = 4_000m;
    private const decimal CarDailyCap = 8_000m;
    private const decimal SuvDailyCap = 12_000m;

    private const int GracePeriodMinutes = 30;
    private const decimal OvernightFlatFee = 2_000m;
    private const int OvernightHourThreshold = 22;

    private const decimal WeekendSurchargeRate = 0.20m;
    private const decimal HolidaySurchargeRate = 0.50m;

    private const decimal SilverDiscountRate = 0.10m;
    private const decimal GoldDiscountRate = 0.25m;
    private const decimal PlatinumDiscountRate = 0.40m;

    private const decimal LostTicketPenalty = 20_000m;

    public ParkingFeeResult CalculateFee(
        VehicleType vehicleType, MembershipTier membership,
        DateTime checkIn, DateTime checkOut,
        bool isLostTicket = false, bool isHoliday = false)
    {
        if (checkOut < checkIn)
            throw new ArgumentException("Check-out cannot be before check-in.");

        var duration = checkOut - checkIn;
        int totalMinutes = (int)duration.TotalMinutes;

        // Grace period – base fee 0, but lost ticket penalty still applies
        if (totalMinutes <= 30)
        {
            decimal graceTotal = isLostTicket ? LostTicketPenalty : 0m;
            return new ParkingFeeResult { TotalFee = graceTotal };
        }

        int billableMinutes = totalMinutes - 30;
        int billableHours = (int)Math.Ceiling(billableMinutes / 60.0);
        if (billableHours < 1) billableHours = 1;

        decimal hourlyRate = vehicleType switch
        {
            VehicleType.Motorcycle => MotorcycleRatePerHour,
            VehicleType.Car => CarRatePerHour,
            VehicleType.SUV => SuvRatePerHour,
            _ => throw new ArgumentException("Unknown vehicle type")
        };
        decimal baseFee = billableHours * hourlyRate;

        decimal dailyCap = vehicleType switch
        {
            VehicleType.Motorcycle => MotorcycleDailyCap,
            VehicleType.Car => CarDailyCap,
            VehicleType.SUV => SuvDailyCap,
            _ => throw new ArgumentException("Unknown vehicle type")
        };
        if (baseFee > dailyCap) baseFee = dailyCap;

        decimal surcharge = 0m;
        bool isWeekend = checkIn.DayOfWeek == DayOfWeek.Saturday
                         || checkIn.DayOfWeek == DayOfWeek.Sunday;
        if (isHoliday)
            surcharge = baseFee * HolidaySurchargeRate;
        else if (isWeekend)
            surcharge = baseFee * WeekendSurchargeRate;

        decimal discountRate = membership switch
        {
            MembershipTier.Silver => SilverDiscountRate,
            MembershipTier.Gold => GoldDiscountRate,
            MembershipTier.Platinum => PlatinumDiscountRate,
            _ => 0m
        };
        decimal discount = (baseFee + surcharge) * discountRate;

        decimal overnightFee = 0m;
        if (checkOut.TimeOfDay >= new TimeSpan(OvernightHourThreshold, 0, 0)
            || checkOut.Date > checkIn.Date)
        {
            overnightFee = OvernightFlatFee;
        }

        decimal lostTicketFee = isLostTicket ? LostTicketPenalty : 0m;

        decimal total = baseFee + surcharge - discount + overnightFee + lostTicketFee;
        if (total < 0) total = 0;

        return new ParkingFeeResult { TotalFee = total };
    }
}