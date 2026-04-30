$src = "B:\github\dnspy-mcp-extension\bin\Release\net10.0-windows"
$dst = "B:\github\dnSpy\dnSpy\dnSpy\bin\Release\net10.0-windows\Extensions\MalwareMCP"

robocopy $src $dst /MIR | Out-Null

# The extension hosts an ASP.NET Core MCP endpoint inside dnSpy's process.
# dnSpy does not carry Microsoft.AspNetCore.App, so copy the shared framework DLLs.
$aspSharedRoot = "C:\Program Files\dotnet\shared\Microsoft.AspNetCore.App"
if (Test-Path $aspSharedRoot) {
	$aspVersionDir = Get-ChildItem $aspSharedRoot -Directory |
		Where-Object { $_.Name -like "10.0.*" } |
		Sort-Object { [version]$_.Name } -Descending |
		Select-Object -First 1

	if ($aspVersionDir) {
		Copy-Item (Join-Path $aspVersionDir.FullName "*.dll") $dst -Force
	}
}