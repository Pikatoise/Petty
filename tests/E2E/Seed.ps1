param(
    [uri]$Server = 'http://127.0.0.1:5187',
    [switch]$LettersOnly
)

$ErrorActionPreference = 'Stop'
if (-not $Server.IsLoopback -or $Server.Scheme -ne 'http' -or
    $Server.AbsolutePath -ne '/' -or $Server.Query -or $Server.UserInfo -or $Server.Fragment) {
    throw 'This disposable fixture accepts only a loopback HTTP origin.'
}
$origin = $Server.AbsoluteUri.TrimEnd('/')
$password = 'E2TestPassword2026'

function Invoke-E2Request($method, $path, $body, $headers) {
    $request = @{
        Method = $method
        Uri = $origin + $path
        ContentType = 'application/json; charset=utf-8'
        Headers = $headers
    }
    if ($null -ne $body) {
        $request.Body = [Text.Encoding]::UTF8.GetBytes((ConvertTo-Json -InputObject $body -Depth 6 -Compress))
    }
    Invoke-RestMethod @request
}

Invoke-E2Request 'GET' '/health/ready' $null @{} | Out-Null
$accounts = @{}
$logins = if ($LettersOnly) { @('e2_cat', 'e2_dog') } else { @('e2_owner', 'e2_cat', 'e2_dog') }
foreach ($login in $logins) {
    if (-not $LettersOnly) {
        Invoke-E2Request 'POST' '/api/v1/auth/register' @{
            login = $login; password = $password; confirmPassword = $password
        } @{} | Out-Null
    }
    $tokens = Invoke-E2Request 'POST' '/api/v1/auth/login' @{ login = $login; password = $password } @{}
    $accounts[$login] = @{ Authorization = 'Bearer ' + $tokens.accessToken }
}

if (-not $LettersOnly) {
    Invoke-E2Request 'PUT' '/api/v1/me/pet' @{
        petName = 'Postcat'; assetId = 'british-shorthair-blue'
    } $accounts.e2_cat | Out-Null
    Invoke-E2Request 'PUT' '/api/v1/me/pet' @{
        petName = 'Postdog'; assetId = 'dachshund-silver-dapple'
    } $accounts.e2_dog | Out-Null
    foreach ($sender in @('e2_cat', 'e2_dog')) {
        $invitation = Invoke-E2Request 'POST' '/api/v1/friends/requests' @{ login = 'e2_owner' } $accounts[$sender]
        Invoke-E2Request 'POST' ("/api/v1/friends/requests/$($invitation.requestId)/accept") $null $accounts.e2_owner | Out-Null
    }
}

foreach ($sender in @('e2_cat', 'e2_dog')) {
    $friends = Invoke-E2Request 'GET' '/api/v1/friends' $null $accounts[$sender]
    $owner = @($friends | Where-Object { $_.login -eq 'e2_owner' })
    if ($owner.Count -ne 1) { throw "Expected accepted friendship with e2_owner for $sender." }
    $count = if ($sender -eq 'e2_cat') { 3 } else { 1 }
    for ($number = 1; $number -le $count; $number++) {
        Invoke-E2Request 'POST' '/api/v1/letters' @{
            recipientUserId = $owner[0].userId
            body = "$sender letter $number of $count. Open to mark read; close the window to let the visitor leave."
            clientRequestId = [Guid]::NewGuid().ToString()
        } $accounts[$sender] | Out-Null
    }
}
Write-Output 'Added three cat letters followed by one dog letter. Test login: e2_owner / E2TestPassword2026.'
Write-Output 'This prepares API fixtures; it does not establish Windows visual acceptance.'
