using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using SIGER.Application.Base;

namespace SIGER.Desktop.Services;

internal sealed class ApiException(string message, HttpStatusCode? statusCode = null) : Exception(message)
{
    internal HttpStatusCode? StatusCode { get; } = statusCode;
}

internal sealed class ApiClient(HttpClient http, SessionService session)
{
    internal const string BaseUrlVariable = "SIGER_API_BASE_URL";
    private const string DefaultBaseUrl = "http://localhost:5179/";
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(15);
    internal static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters =
        {
            new JsonStringEnumConverter(allowIntegerValues: false),
            new PaginatedResultJsonConverterFactory()
        }
    };

    internal static Uri ReadBaseAddress(string? configuredUrl)
    {
        var value = string.IsNullOrWhiteSpace(configuredUrl) ? DefaultBaseUrl : configuredUrl.Trim();
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || string.IsNullOrEmpty(uri.Host)
            || (uri.Scheme != Uri.UriSchemeHttps && !(uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback))
            || uri.UserInfo.Length > 0 || uri.Query.Length > 0 || uri.Fragment.Length > 0)
            throw new ApiException("Configura SIGER_API_BASE_URL con una dirección HTTPS o HTTP local válida.");

        return new UriBuilder(uri) { Path = uri.AbsolutePath.TrimEnd('/') + "/" }.Uri;
    }

    internal Task<T> SendAsync<T>(HttpMethod method, string path, object? body = null,
        bool authenticated = true, CancellationToken cancellationToken = default) =>
        SendCoreAsync(method, path, body, authenticated, ReadJsonAsync<T>, cancellationToken);

    internal Task SendAsync(HttpMethod method, string path, object? body = null,
        bool authenticated = true, CancellationToken cancellationToken = default) =>
        SendCoreAsync(method, path, body, authenticated, static (_, _) => Task.FromResult(true), cancellationToken);

    private async Task<T> SendCoreAsync<T>(HttpMethod method, string path, object? body, bool authenticated,
        Func<HttpResponseMessage, CancellationToken, Task<T>> readResponse, CancellationToken cancellationToken)
    {
        // Use an absolute URI only after proving the path stays inside the configured API.
        var root = http.BaseAddress ?? throw new InvalidOperationException("The desktop API base address is missing.");
        if (!Uri.TryCreate(root, path, out var destination) || !root.IsBaseOf(destination)
            || destination.UserInfo.Length > 0)
            throw new ArgumentException("The request must target the configured API.", nameof(path));

        cancellationToken.ThrowIfCancellationRequested();
        var credential = authenticated ? session.GetCredential() : null;
        if (authenticated && credential is null)
            throw new ApiException("Tu sesión terminó. Inicia sesión de nuevo.", HttpStatusCode.Unauthorized);

        using var request = new HttpRequestMessage(method, destination);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (credential is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credential.AccessToken);
        if (body is not null) request.Content = JsonContent.Create(body, options: JsonOptions);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(RequestTimeout);

        try
        {
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token)
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                if (response.StatusCode == HttpStatusCode.Unauthorized && credential is not null)
                    session.Invalidate(credential);
                if (response.StatusCode == HttpStatusCode.Conflict && method == HttpMethod.Post &&
                    destination.AbsolutePath.EndsWith("/api/v1/payments", StringComparison.Ordinal))
                {
                    try
                    {
                        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(timeout.Token).ConfigureAwait(false);
                        if (problem.ValueKind == JsonValueKind.Object && problem.TryGetProperty("detail", out var detail) &&
                            detail.ValueKind == JsonValueKind.String &&
                            PaymentApiService.TranslatePaymentConflict(detail.GetString()) is { } message)
                            throw new ApiException(message, response.StatusCode);
                    }
                    catch (JsonException) { }
                    catch (InvalidOperationException) { }
                }
                throw ErrorFor(response.StatusCode);
            }

            return await readResponse(response, timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new ApiException("La solicitud tardó demasiado. Comprueba la conexión e inténtalo de nuevo.");
        }
        catch (Exception error) when (error is HttpRequestException or IOException)
        {
            throw new ApiException("No se pudo conectar con SIGER. Comprueba tu conexión e inténtalo de nuevo.");
        }
        catch (JsonException)
        {
            throw new ApiException("SIGER devolvió una respuesta inesperada. Inténtalo de nuevo.");
        }
    }

    private static async Task<T> ReadJsonAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken).ConfigureAwait(false)
                ?? throw new ApiException("SIGER devolvió una respuesta vacía. Inténtalo de nuevo.");
        }
        catch (InvalidOperationException)
        {
            throw new ApiException("SIGER devolvió una respuesta que no se pudo leer. Inténtalo de nuevo.");
        }
    }

    private static ApiException ErrorFor(HttpStatusCode status) => new(status switch
    {
        >= HttpStatusCode.MultipleChoices and < HttpStatusCode.BadRequest =>
            "La API cambió de dirección. Configura SIGER_API_BASE_URL con su dirección final.",
        HttpStatusCode.BadRequest => "Revisa los datos enviados e inténtalo de nuevo.",
        HttpStatusCode.Unauthorized => "Tu sesión terminó. Inicia sesión de nuevo.",
        HttpStatusCode.Forbidden => "Acceso denegado. Tu rol no tiene permiso para realizar esta operación.",
        HttpStatusCode.NotFound => "No se encontró lo solicitado.",
        HttpStatusCode.Conflict => "La información cambió. Actualiza los datos antes de continuar.",
        HttpStatusCode.TooManyRequests => "Hay demasiados intentos. Espera un momento antes de volver a intentarlo.",
        >= HttpStatusCode.InternalServerError => "SIGER no está disponible por el momento. Inténtalo más tarde.",
        _ => "SIGER no pudo completar la solicitud. Inténtalo de nuevo."
    }, status);

    private sealed class PaginatedResultJsonConverterFactory : JsonConverterFactory
    {
        public override bool CanConvert(Type typeToConvert) =>
            typeToConvert.IsGenericType && typeToConvert.GetGenericTypeDefinition() == typeof(PaginatedResult<>);

        public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
        {
            var itemType = typeToConvert.GetGenericArguments()[0];
            var converterType = typeof(PaginatedResultJsonConverter<>).MakeGenericType(itemType);
            return (JsonConverter)Activator.CreateInstance(converterType)!;
        }

        private sealed class PaginatedResultJsonConverter<TItem> : JsonConverter<PaginatedResult<TItem>>
        {
            private sealed class PagePayload
            {
                public List<TItem>? Items { get; set; }
                public int TotalCount { get; set; }
                public int PageNumber { get; set; }
                public int PageSize { get; set; }
            }

            public override PaginatedResult<TItem>? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            {
                var payload = JsonSerializer.Deserialize<PagePayload>(ref reader, options);
                if (payload is null) return null;
                if (payload.Items is null || payload.TotalCount < 0 || payload.PageNumber < 1 || payload.PageSize < 1)
                    throw new JsonException("Invalid paginated payload.");

                return new PaginatedResult<TItem>(payload.Items, payload.TotalCount, payload.PageNumber, payload.PageSize);
            }

            public override void Write(Utf8JsonWriter writer, PaginatedResult<TItem> value, JsonSerializerOptions options) =>
                JsonSerializer.Serialize(writer, new
                {
                    value.Items,
                    value.PageNumber,
                    value.PageSize,
                    value.TotalCount,
                    value.TotalPages
                }, options);
        }
    }
}
