namespace LineReservation.Models
{
    public class LineLoginOptions
    {
    public const string SectionName = "LineLogin";
    public string ChannelId { get; set; } = "";
    public string ChannelSecret { get; set; } = "";
    public string RedirectUri { get; set; } = "";
    public string SuccessRedirectUrl { get; set; } = "";
    public string Scope { get; set; } = "profile openid email";
    }
}
