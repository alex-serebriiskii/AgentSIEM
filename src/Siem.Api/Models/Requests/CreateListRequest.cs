namespace Siem.Api.Models.Requests;

public class CreateListRequest
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public List<string> Members { get; set; } = [];
}
