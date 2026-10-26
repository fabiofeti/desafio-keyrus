using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace CashFlow.Modules.Transactions.Application.Transactions.Create;

/// <summary>
/// SHA-256 of the normalised payload, without the key. Normalised so that the
/// same request sent twice hashes the same: the amount always with two decimals,
/// the instant always in UTC (10:00-03:00 and 13:00Z are one request).
/// </summary>
internal static class RequestFingerprint
{
    public static string Of(CreateTransactionCommand command)
    {
        string normalised = string.Join(
            '|',
            command.Type,
            command.Amount.ToString("0.00", CultureInfo.InvariantCulture),
            command.OccurredAt.UtcDateTime.ToString("O", CultureInfo.InvariantCulture),
            command.Description ?? string.Empty);

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(normalised)));
    }
}
