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
            var categoryTerms = Category switch
            {
                "Interiors" => "architectural interior",
                "Details" => "architectural detail material",
                "Drawings" => "architectural drawing sketch plan section",
                "Models" => "architectural model maquette",
                _ => "architecture building exterior"
            };
            return Architect.Length == 0
                ? categoryTerms
                : $"\"{Architect}\" {categoryTerms}";
        }
    }
}
