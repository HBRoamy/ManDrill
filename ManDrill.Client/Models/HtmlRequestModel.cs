using System.Text.Json.Serialization;

public class HtmlRequestModel {
    [JsonPropertyName("htmlContent")]
    public string HtmlContent { get; set; }
}
