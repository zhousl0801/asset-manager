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
    FundQuote,
    GoldBuy,
    GoldSell,
    GoldQuote,
    GoldCorrection,
    ProvidentOpening,
    GoldOpening,
    FundCorrection
}
