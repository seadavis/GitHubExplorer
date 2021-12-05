 param (
    [Parameter(Mandatory=$true)][string]$clone_url,
    [Parameter(Mandatory=$true)][string]$root_folder
 )

$folder_guid = [guid]::NewGuid()

New-Item -Path $root_folder -Name $folder_guid -ItemType "directory"
$full_folder_name =  "$($root_folder)\$($folder_guid)"

git clone $clone_url $full_folder_name
Write-Host $full_folder_name