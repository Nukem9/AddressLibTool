namespace AddressLibTool;

public static class Utility
{
    public static bool TryExtractNumberInParenthesis(string input, out ulong value)
    {
        int innermost = input.LastIndexOf('(');

        if (innermost == -1)
            throw new FormatException("String doesn't contain an opening parenthesis");

        int closingBrace = input[innermost..].IndexOf(')');

        if (closingBrace == -1)
            throw new FormatException("String doesn't contain a closing parenthesis after an opening");

        var valueString = input.Substring(innermost + 1, closingBrace - 1).Trim();
        value = 0;

        // If nothing is contained inside or it's not a valid number, skip it
        if (string.IsNullOrEmpty(valueString))
            return false;

        if (!ulong.TryParse(valueString, System.Globalization.NumberStyles.Integer, null, out value))
            return false;

        return true;
    }
}
