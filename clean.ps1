Get-ChildItem -Include bin,obj -Recurse | ForEach-Object ($_) { Remove-Item $_.fullname -Force -Recurse }
