using System.Globalization;
using System.Text.Json.Serialization;

namespace medrec.Models;

public sealed class PrintLayoutBlock
{
    public string Key { get; set; } = string.Empty;
    public string Type { get; set; } = "Field";
    public string Label { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public string? ImageUrl { get; set; }
    public decimal X { get; set; }
    public decimal Y { get; set; }
    public decimal Width { get; set; }
    public decimal Height { get; set; }
    public int FontSize { get; set; } = 11;
    public int LineWidth { get; set; } = 2;
    public string TextAlign { get; set; } = "Left";
    public string FontWeight { get; set; } = "Normal";
    public bool IsVisible { get; set; } = true;

    [JsonIgnore]
    public string Style => string.Create(CultureInfo.InvariantCulture,
        $"left:{X:0.##}mm;top:{Y:0.##}mm;width:{Width:0.##}mm;height:{Height:0.##}mm;font-size:{FontSize}px;text-align:{CssTextAlign};font-weight:{CssFontWeight};z-index:{CssZIndex};");

    [JsonIgnore]
    public string CssTextAlign => TextAlign.Equals("Center", StringComparison.OrdinalIgnoreCase)
        ? "center"
        : TextAlign.Equals("Right", StringComparison.OrdinalIgnoreCase)
            ? "right"
            : "left";

    [JsonIgnore]
    public string CssFontWeight => FontWeight.Equals("Bold", StringComparison.OrdinalIgnoreCase) ? "800" : "400";

    [JsonIgnore]
    public int CssZIndex => Type.Equals("Text", StringComparison.OrdinalIgnoreCase) || Type.Equals("Image", StringComparison.OrdinalIgnoreCase)
        ? 2
        : 1;
}
