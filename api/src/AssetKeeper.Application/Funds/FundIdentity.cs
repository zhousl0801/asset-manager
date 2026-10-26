using System.Text;
using AssetKeeper.Domain.Assets;

namespace AssetKeeper.Application.Funds;

public static class FundIdentity
{
    public static string Normalize(string? value)
    {
        return string.Concat((value ?? string.Empty).Normalize(NormalizationForm.FormKC)
            .Where(character => !char.IsWhiteSpace(character))).ToUpperInvariant();
    }

    public static string ClassKey(FundPosition position)
    {
        return ClassKey(position.Name, position.Code, position.ShareClass);
    }

    public static string ClassKey(string name, string? code, string? shareClass)
    {
        var identity = string.IsNullOrWhiteSpace(code) ? $"name:{Normalize(name)}" : $"code:{Normalize(code)}";
        return $"{identity}|class:{Normalize(shareClass)}";
    }

    public static IReadOnlyDictionary<Guid, Guid?> EffectiveGroupIds(IReadOnlyList<FundPosition> positions)
    {
        var keys = FamilyKeys(positions);
        var groupIds = positions.GroupBy(position => keys[position.Id]).ToDictionary(group => group.Key,
            group => group.OrderBy(position => position.ShareClass).ThenBy(position => position.Platform).ThenBy(position => position.Id)
                .Select(position => position.FundGroupId).FirstOrDefault(id => id is not null));

        return positions.ToDictionary(position => position.Id, position => groupIds[keys[position.Id]]);
    }

    // 基金名称连接不同类别，同代码/类别连接名称别名；不修改来源记录。
    public static IReadOnlyDictionary<Guid, string> FamilyKeys(IReadOnlyList<FundPosition> positions)
    {
        var names = positions.Select(position => Normalize(position.Name)).Distinct().ToDictionary(name => name, name => name);

        string Root(string name)
        {
            while (names[name] != name)
            {
                name = names[name];
            }

            return name;
        }

        foreach (var aliases in positions.Where(position => !string.IsNullOrWhiteSpace(position.Code)).GroupBy(ClassKey))
        {
            var roots = aliases.Select(position => Root(Normalize(position.Name))).Distinct().Order().ToList();

            foreach (var root in roots.Skip(1))
            {
                names[root] = roots[0];
            }
        }

        return positions.ToDictionary(position => position.Id, position => Root(Normalize(position.Name)));
    }
}
