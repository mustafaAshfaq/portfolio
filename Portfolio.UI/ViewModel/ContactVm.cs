using System.ComponentModel.DataAnnotations;

namespace Portfolio.UI.ViewModel;

public class ContactModel
{
    private string _name = string.Empty;
    private string _email = string.Empty;
    private string _message = string.Empty;

    [Required]
    [StringLength(100)]
    public string name { get => _name; set => _name = value?.Trim() ?? string.Empty; }

    [EmailAddress]
    [Required]
    [StringLength(254)]
    public string email { get => _email; set => _email = value?.Trim() ?? string.Empty; }

    [Required]
    [StringLength(5000)]
    public string message { get => _message; set => _message = value?.Trim() ?? string.Empty; }

    public string honeypot { get; set; } = string.Empty;
}
