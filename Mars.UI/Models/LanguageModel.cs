namespace Mars.UI.Models;

public class LanguageModel
{
    public string Name { get; set; } 
    public string Code { get; set; } 
    
    public override string ToString()
    {
        return Name;
    }
}
