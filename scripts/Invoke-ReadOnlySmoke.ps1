# Read-only, opt-in smoke test. Not invoked by dotnet test.
# Requires the API to have been built and its existing User Secrets configured.
[CmdletBinding()]
param([ValidateSet('Debug', 'Release')][string]$Configuration = 'Release')
$ErrorActionPreference = 'Stop'
$taskRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$apiRoot = Join-Path $taskRoot 'SIGER.API'
[xml]$project = Get-Content -LiteralPath (Join-Path $apiRoot 'SIGER.API.csproj')
$secretId = [string]($project.Project.PropertyGroup.UserSecretsId | Where-Object { $_ } | Select-Object -First 1)
$secretFile = Join-Path ([Environment]::GetFolderPath('ApplicationData')) "Microsoft/UserSecrets/$secretId/secrets.json"
if (-not (Test-Path -LiteralPath $secretFile)) {
    Write-Output 'Smoke test omitted: existing API User Secrets unavailable.'
    return
}
$settings = Get-Content -LiteralPath $secretFile -Raw | ConvertFrom-Json -AsHashtable
$required = @('ConnectionStrings:SIGERDatabase', 'Supabase:Url', 'Supabase:ServiceRoleKey')
if (@($required | Where-Object { [string]::IsNullOrWhiteSpace($settings[$_]) }).Count -gt 0) {
    Write-Output 'Smoke test omitted: required keys unavailable in existing API User Secrets.'
    return
}
$apiDll = Join-Path $apiRoot "bin/$Configuration/net10.0/SIGER.API.dll"
if (-not (Test-Path -LiteralPath $apiDll)) { throw 'Build SIGER.API before running this smoke test.' }

$listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
$listener.Start()
$port = $listener.LocalEndpoint.Port
$listener.Stop()
$baseUrl = "http://127.0.0.1:$port"
$start = [Diagnostics.ProcessStartInfo]::new('dotnet')
$start.ArgumentList.Add($apiDll)
$start.WorkingDirectory = $apiRoot
$start.UseShellExecute = $false
$start.CreateNoWindow = $true
$start.RedirectStandardOutput = $true
$start.RedirectStandardError = $true
$start.Environment['ASPNETCORE_ENVIRONMENT'] = 'Development'
$start.Environment['DOTNET_ENVIRONMENT'] = 'Development'
$start.Environment['ASPNETCORE_URLS'] = $baseUrl
$process = $null
$handler = [Net.Http.HttpClientHandler]::new()
$handler.UseProxy = $false
$client = [Net.Http.HttpClient]::new($handler)
$client.Timeout = [TimeSpan]::FromSeconds(20)
try {
    $process = [Diagnostics.Process]::Start($start)
    # Drain but never display process logs: startup failures may contain configuration details.
    $stdout = $process.StandardOutput.ReadToEndAsync()
    $stderr = $process.StandardError.ReadToEndAsync()
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds(25)
    $ready = $false
    while ([DateTimeOffset]::UtcNow -lt $deadline -and -not $process.HasExited) {
        try {
            $probe = $client.GetAsync("$baseUrl/swagger/index.html").GetAwaiter().GetResult()
            $ready = $probe.IsSuccessStatusCode
            $probe.Dispose()
            if ($ready) { break }
        } catch { }
        Start-Sleep -Milliseconds 200
    }
    if (-not $ready) {
        Write-Output 'API startup: not verified. Process logs withheld to protect secrets.'
        return
    }
    Write-Output 'API startup: OK; GET /swagger/index.html: 200'
    foreach ($path in @('/openapi/v1.json', '/api/v1/products/available')) {
        try {
            $response = $client.GetAsync("$baseUrl$path").GetAwaiter().GetResult()
            $status = [int]$response.StatusCode
            $suffix = ''
            if ($path -eq '/api/v1/products/available' -and $response.IsSuccessStatusCode) {
                $body = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
                $json = [Text.Json.JsonDocument]::Parse($body)
                $suffix = "; JSON kind=$($json.RootElement.ValueKind); count=$($json.RootElement.GetArrayLength())"
                $json.Dispose()
            }
            Write-Output "GET $path : $status$suffix"
            $response.Dispose()
        } catch {
            Write-Output "GET $path : failed; details withheld."
        }
    }
    # Public signing keys only. No Authorization or apikey headers are sent.
    $jwksUrl = ([string]$settings['Supabase:Url']).TrimEnd('/') + '/auth/v1/.well-known/jwks.json'
    if (([uri]$jwksUrl).Scheme -eq 'https') {
        try {
            $jwksClient = [Net.Http.HttpClient]::new()
            $jwksClient.Timeout = [TimeSpan]::FromSeconds(15)
            try {
                $response = $jwksClient.GetAsync($jwksUrl).GetAwaiter().GetResult()
                if ($response.IsSuccessStatusCode) {
                    $keys = ($response.Content.ReadAsStringAsync().GetAwaiter().GetResult() | ConvertFrom-Json).keys
                    $algorithms = @($keys | ForEach-Object { $_.alg } | Where-Object { $_ -in @('RS256', 'ES256', 'HS256') } | Sort-Object -Unique) -join ','
                    Write-Output "Public JWKS: 200; key count=$(@($keys).Count); recognized algorithms=$algorithms"
                } else { Write-Output "Public JWKS: HTTP $([int]$response.StatusCode)" }
                $response.Dispose()
            } finally { $jwksClient.Dispose() }
        } catch { Write-Output 'Public JWKS: unavailable; details withheld.' }
    }
    Write-Output 'Login not performed by this GET-only script; consult BACKEND_AUDIT.md for authentication evidence.'
    Write-Output 'Only HTTP GET requests performed. No remote data or schema changes requested.'
} finally {
    $client.Dispose()
    if ($null -ne $process) {
        if (-not $process.HasExited) { $process.Kill(); $process.WaitForExit(5000) | Out-Null }
        $process.Dispose()
    }
    $settings = $null
}
