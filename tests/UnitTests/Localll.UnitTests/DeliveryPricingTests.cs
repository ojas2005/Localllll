using Localll.Delivery.API.Domain;
using Xunit;

namespace UnitTests;

public class DeliveryPricingTests
{
    [Fact]
    public void Parcel_BaseCase_UsesBasePlusDistance()
    {
        // 30 base + 5km * 8 = 70, first kg free
        var charge = DeliveryPricing.Calculate(DeliveryOrderType.Parcel, distanceKm: 5, weightKg: 1);
        Assert.Equal(70m, charge);
    }

    [Fact]
    public void Parcel_HeavyPackage_ChargesPerExtraKg()
    {
        // 30 + 2*8 + (3-1)*10 = 66
        var charge = DeliveryPricing.Calculate(DeliveryOrderType.Parcel, distanceKm: 2, weightKg: 3);
        Assert.Equal(66m, charge);
    }

    [Fact]
    public void Grocery_AddsSurcharge()
    {
        var parcel = DeliveryPricing.Calculate(DeliveryOrderType.Parcel, 4, 1);
        var grocery = DeliveryPricing.Calculate(DeliveryOrderType.Grocery, 4, 1);
        Assert.Equal(DeliveryPricing.GrocerySurcharge, grocery - parcel);
    }

    [Fact]
    public void WeightBelowOneKg_DoesNotChargeWeight()
    {
        var charge = DeliveryPricing.Calculate(DeliveryOrderType.Parcel, 0, 0.5);
        Assert.Equal(DeliveryPricing.BaseCharge, charge);
    }

    [Theory]
    [InlineData(-1, 1)]
    [InlineData(1, -1)]
    public void NegativeInputs_Throw(double distance, double weight)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            DeliveryPricing.Calculate(DeliveryOrderType.Parcel, distance, weight));
    }
}
