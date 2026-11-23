using Rail.Api.Models.Metro;
using Rail.Api.Services;
using Xunit;

namespace Rail.Tests.Api;

public sealed class TrafficEstimateTests
{
    private static readonly DateTime Evening = new(2026, 11, 14, 19, 23, 0);
    private static TrafficEstimate Model(string code = ServiceDayType.SaturdayToWednesday) => new(_ => code);

    [Theory]
    [InlineData(ServiceDayType.SaturdayToWednesday, 3.6166666667)]
    [InlineData(ServiceDayType.Thursday, 3.385)]
    [InlineData(ServiceDayType.FridayOrHoliday, 2.3616666667)]
    public void InterpolatesTheAgreedCurves(string code, double expected)
        => Assert.Equal(expected, Model(code).Coefficient(Evening, "car"), 8);

    [Theory]
    [InlineData("car")]
    [InlineData("motorcycle")]
    public void AdvancesActualTimeAndMatchesIndependentFineStepIntegration(string mode)
    {
        var model = Model();
        var time = Evening;
        const double step = .05;
        for (var i = 0; i < 7200; i++)
        {
            var initial = model.Coefficient(time, mode);
            time = time.AddSeconds(step * model.Coefficient(time.AddSeconds(step * initial / 2), mode));
        }
        var seconds = model.Seconds(360, Evening, mode);
        Assert.InRange(Math.Abs(seconds - (time - Evening).TotalSeconds), 0, .01);
        Assert.True(seconds < 360 * model.Coefficient(Evening, mode));
        Assert.Equal(seconds, model.Seconds(360, Evening.AddSeconds(seconds), mode, true), 5);
    }

    [Fact]
    public void CrossesMidnightAndDayTypesAndPreservesWalking()
    {
        var seen = new HashSet<DateOnly>();
        var model = new TrafficEstimate(day => { seen.Add(day); return ServiceDayType.DefaultCodeFor(day); });
        var start = new DateTime(2026, 11, 19, 23, 58, 0); // Thursday into Friday.
        var seconds = model.Seconds(600, start, "car");
        Assert.Contains(new DateOnly(2026, 11, 19), seen);
        Assert.Contains(new DateOnly(2026, 11, 20), seen);
        Assert.Equal(seconds, model.Seconds(600, start.AddSeconds(seconds), "car", true), 5);
        Assert.Equal(600, model.Seconds(600, start, "walk"));
        Assert.Equal(0, model.Seconds(0, start, "car"));
    }

    [Fact]
    public void MotorcycleRangeAndHolidayPrecedenceAreExplicit()
    {
        var peak = Evening.Date.AddHours(18);
        Assert.Equal(5, Model().Coefficient(peak, "car"));
        Assert.Equal(2, Model().Coefficient(peak, "motorcycle"));
        Assert.Equal(1, Model().Coefficient(Evening.Date, "motorcycle"));
        Assert.Equal(2.5, Model(ServiceDayType.FridayOrHoliday).Coefficient(peak, "car"));
    }

    [Fact]
    public void ResponseKeepsBaselineAndConsistentInstructionDurationsWithoutMutatingCachedPath()
    {
        var path = new StreetPath("car", 1000, 360, [[51.4, 35.7], [51.41, 35.71]],
            [new("First", 500, 180), new("Second", 500, 180)], "graphhopper");
        var adjusted = Model().Apply(path, Evening);
        Assert.Equal(360, adjusted.BaselineSeconds);
        Assert.Equal("assumed_day_hour_v1", adjusted.TrafficModel);
        Assert.Equal(adjusted.Seconds, adjusted.Steps.Sum(s => s.Seconds));
        Assert.True(adjusted.Steps[0].Seconds > adjusted.Steps[1].Seconds);
        Assert.Equal(360, path.Seconds);
        Assert.Equal("none", path.TrafficModel);
    }
}
