using Npgsql;

namespace SIGER.Infrastructure.Persistence;

internal sealed class PostgreSqlEnumNameTranslator : INpgsqlNameTranslator
{
    private readonly IReadOnlyDictionary<string, string> _labels;

    public PostgreSqlEnumNameTranslator(IReadOnlyDictionary<string, string> labels)
    {
        _labels = labels;
    }

    public string TranslateTypeName(string clrName) => clrName;

    public string TranslateMemberName(string clrName)
        => _labels.TryGetValue(clrName, out var label) ? label : clrName;
}
