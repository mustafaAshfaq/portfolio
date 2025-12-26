using System;
using System.ComponentModel.DataAnnotations;

namespace Portfolio.UI.ViewModel;

public class ContactModel
{
    [Required]
    public string name { get; set; }

    [EmailAddress]
    [Required]
    public string email { get; set; }
    public string message { get; set; }
}
