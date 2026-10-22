namespace AssetKeeper.Application.Assets;

public static class AssetValidation
{
    public static List<string> ValidateBusinessDate(
        DateOnly businessDate,
        DateOnly ledgerStartDate,
        DateOnly businessToday)
    {
        var errors = new List<string>();

        if (businessDate == default)
        {
            errors.Add("业务日期无效。");
        }
        else if (businessDate < ledgerStartDate)
        {
            errors.Add("业务日期不能早于账本起始日期。");
        }
        else if (businessDate > businessToday)
        {
            errors.Add("业务日期不能晚于今天。");
        }

        return errors;
    }

    public static List<string> ValidateValueDate(DateOnly valueDate, DateOnly businessToday)
    {
        var errors = new List<string>();

        if (valueDate == default)
        {
            errors.Add("日期无效。");
        }
        else if (valueDate > businessToday)
        {
            errors.Add("日期不能晚于今天。");
        }

        return errors;
    }
}
