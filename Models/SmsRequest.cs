namespace ParcelAPI.Models;

public class SmsRequest
{
    public string Phone { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? DocumentNo { get; set; }
}

public class SmsBulkRequest
{
    public List<SmsRequest> Messages { get; set; } = new();
}

public class SmsResponse
{
    public int Code { get; set; }
    public string Desc { get; set; } = string.Empty;
    public List<SmsResult> Results { get; set; } = new();
}

public class SmsResult
{
    public string Phone { get; set; } = string.Empty;
    public bool Success { get; set; }
    public string? Error { get; set; }
}
