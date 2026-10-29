using AssetKeeper.Application.Quotes;

namespace AssetKeeper.Tests.Funds;

public sealed class FundProfileRulesTests
{
    [Theory]
    [InlineData("大成纳斯达克100ETF联接(QDII)A", "A")]
    [InlineData("浦银全球智能科技股票(QDII)C", "C")]
    [InlineData("国泰纳斯达克100指数", null)]
    [InlineData("华夏标普500ETF联接(QDII)", null)]
    [InlineData("华夏中证A500ETF", null)]
    [InlineData("易方达中证A500ETF联接A", "A")]
    [InlineData("华宝中证100A", "A")]
    [InlineData("华夏某海外基金QDII", null)]
    public void DetectShareClass_ReadsTrailingClassSuffix(string name, string? expected)
    {
        Assert.Equal(expected, FundProfileRules.DetectShareClass(name));
    }

    [Theory]
    [InlineData("大成纳斯达克100ETF联接(QDII)A", "指数型-海外股票", true)]
    [InlineData("浦银全球智能科技股票(QDII)C", "混合型-偏股", true)]
    [InlineData("华夏标普500ETF联接(QDII)", "指数型-海外股票", true)]
    [InlineData("易方达中证A500ETF联接A", "指数型-股票", false)]
    [InlineData("招商中证白酒指数A", "指数型-股票", false)]
    public void IsQdii_UsesNameAndFundType(string name, string fundType, bool expected)
    {
        Assert.Equal(expected, FundProfileRules.IsQdii(name, fundType));
    }

    [Fact]
    public void BaseName_RemovesDetectedClassSuffixOnly()
    {
        Assert.Equal("大成纳斯达克100ETF联接(QDII)", FundProfileRules.BaseName("大成纳斯达克100ETF联接(QDII)A", "A"));
        Assert.Equal("华夏标普500ETF联接(QDII)", FundProfileRules.BaseName("华夏标普500ETF联接(QDII)", null));
    }

    [Theory]
    [InlineData(true, 2)]
    [InlineData(false, 1)]
    public void ConfirmationDelayDays_FollowsFundType(bool isQdii, int expected)
    {
        Assert.Equal(expected, FundProfileRules.ConfirmationDelayDays(isQdii));
    }
}
