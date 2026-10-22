namespace AssetKeeper.Domain.Assets;

public enum AssetTransactionKind
{
    ProvidentDeposit,
    ProvidentWithdrawal,
    FundOpening,
    FundBuy,
    FundBuyPending,
    FundBuyConfirm,
    FundSell,
    FundSellApply,
    FundSellConfirm,
    FundSellArrival,
    FundQuote,
    GoldBuy,
    GoldSell,
    GoldSellPending,
    GoldSellArrival,
    GoldQuote,
    GoldCorrection,
    ProvidentOpening,
    GoldOpening,
    FundCorrection
}
