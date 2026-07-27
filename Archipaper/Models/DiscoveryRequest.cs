namespace Archipaper.Models;

public sealed record DiscoveryRequest(
    string Architect,
    string Category,
    bool IsStrict)
{
    public string DisplayLabel
    {
        get
        {
            if (Architect.Length > 0 && Category.Length > 0) return $"{Architect} · {Category}";
            if (Architect.Length > 0) return Architect;
            return Category;
        }
    }

    public string QueryText
    {
        get
        {
            var categoryTerms = CategoryTerms;
            return Architect.Length == 0
                ? categoryTerms
                : $"\"{Architect}\" {categoryTerms}";
        }
    }

    public IReadOnlyList<string> QueryVariants
    {
        get
        {
            if (Architect.Length == 0) return [CategoryTerms];

            var exactArchitect = $"\"{Architect}\"";
            return Category switch
            {
                "Interiors" => [exactArchitect + " architectural interior", exactArchitect + " interior design"],
                "Details" => [exactArchitect + " architectural detail", exactArchitect + " material detail"],
                "Drawings" => [exactArchitect + " architectural drawing", exactArchitect + " sketch plan section"],
                "Models" => [exactArchitect + " architectural model", exactArchitect + " maquette"],
                _ => [exactArchitect + " architecture", exactArchitect + " building", exactArchitect]
            };
        }
    }

    private string CategoryTerms => Category switch
    {
        "Interiors" => "architectural interior",
        "Details" => "architectural detail material",
        "Drawings" => "architectural drawing sketch plan section",
        "Models" => "architectural model maquette",
        _ => "architecture building exterior"
    };
}
