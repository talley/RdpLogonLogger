namespace RdpSessionsViewer.Models;

public sealed class SessionsViewModel
{
    public IReadOnlyList<string> Headers { get; set; } = [];
    public IReadOnlyList<string[]> Rows { get; set; } = [];
    public string? Error { get; set; }
    public string Search { get; set; } = "";
    public int Sort { get; set; } = 6;
    public string Direction { get; set; } = "desc";
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;
    public int Total { get; set; }
    public int Matched { get; set; }
    public int Pages => Math.Max(1, (int)Math.Ceiling((double)Matched / PageSize));
}
