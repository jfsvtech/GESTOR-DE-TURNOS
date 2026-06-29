namespace GeneradorTurnos.Helpers;

public static class PhoneHelper
{
    public static string Digits(string? phone)
        => new((phone ?? "").Where(char.IsDigit).ToArray());

    public static string? WhatsAppNumber(string? phone)
    {
        var digits = Digits(phone);
        if (string.IsNullOrWhiteSpace(digits)) return null;
        return digits.Length == 10 ? $"57{digits}" : digits;
    }

    public static string? WhatsAppUrl(string? phone)
    {
        var number = WhatsAppNumber(phone);
        return number is null ? null : $"https://wa.me/{number}";
    }
}
