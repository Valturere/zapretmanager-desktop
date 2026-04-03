using System.Net.Http;
using System.Net.Sockets;
using System.Security.Authentication;

namespace ZapretManager.Services;

internal static class NetworkErrorTranslator
{
    public static bool IsNetworkException(Exception exception)
    {
        return EnumerateExceptions(exception).Any(item =>
            item is HttpRequestException or TaskCanceledException or TimeoutException or SocketException or AuthenticationException);
    }

    public static InvalidOperationException CreateGitHubException(Exception exception, string action)
    {
        var all = EnumerateExceptions(exception).ToArray();
        var httpException = all.OfType<HttpRequestException>().FirstOrDefault();
        var timeoutDetected = all.Any(item => item is TaskCanceledException or TimeoutException);
        var sslDetected = all.Any(item => item is AuthenticationException) ||
                          all.Any(item => ContainsAny(item.Message, "ssl", "tls", "certificate", "secure channel", "unexpected eof"));
        var dnsDetected = all.Any(item => item is SocketException) ||
                          all.Any(item => ContainsAny(item.Message, "name resolution", "no such host", "temporary failure", "не удается разрешить", "удаленный хост", "host is known"));

        if (timeoutDetected)
        {
            return new InvalidOperationException(
                $"{action}: GitHub не ответил вовремя. Попробуйте ещё раз чуть позже.",
                exception);
        }

        if (sslDetected)
        {
            return new InvalidOperationException(
                $"{action}: не удалось установить защищённое HTTPS-соединение с GitHub. Проверьте системное время, DNS, прокси или антивирус и повторите попытку.",
                exception);
        }

        if (dnsDetected)
        {
            return new InvalidOperationException(
                $"{action}: не удалось подключиться к GitHub. Проверьте интернет и DNS, затем попробуйте ещё раз.",
                exception);
        }

        if (httpException?.StatusCode is not null)
        {
            return new InvalidOperationException(
                $"{action}: GitHub вернул ошибку HTTP {(int)httpException.StatusCode}. Попробуйте повторить позже.",
                exception);
        }

        return new InvalidOperationException(
            $"{action}: не удалось получить данные из GitHub. Проверьте интернет, DNS и доступ к github.com.",
            exception);
    }

    private static IEnumerable<Exception> EnumerateExceptions(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            yield return current;
        }
    }

    private static bool ContainsAny(string? value, params string[] fragments)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return fragments.Any(fragment => value.Contains(fragment, StringComparison.OrdinalIgnoreCase));
    }
}
