namespace Orchester.Storage;

internal static class WindowsEncryptionQuery {
    // Both paths query Windows' supported encryption interfaces; absence is never treated as decrypted.
    public const string Script="""
function Get-OrchesterEncryption($volume) {
 try {
  $bitlocker=Get-BitLockerVolume -MountPoint $volume.Path -ErrorAction Stop
  if($bitlocker.LockStatus -eq 'Locked'){return 'Locked'}
  if($bitlocker.VolumeStatus -eq 'FullyDecrypted'){return 'Unencrypted'}
  if($bitlocker.ProtectionStatus -eq 'On'){return 'Protected'}
  return 'Suspended'
 } catch {}
 try {
  $letter=if($volume.DriveLetter){([string]$volume.DriveLetter)+':'}else{''}
  $items=@(Get-CimInstance -Namespace 'root/CIMV2/Security/MicrosoftVolumeEncryption' -ClassName Win32_EncryptableVolume -ErrorAction Stop)
  $encrypted=$items|Where-Object {$_.DeviceID -eq $volume.Path -or ($letter -and $_.DriveLetter -eq $letter)}|Select-Object -First 1
  if(!$encrypted){return 'Unknown'}
  $locked=Invoke-CimMethod -InputObject $encrypted -MethodName GetLockStatus -ErrorAction Stop
  $conversion=Invoke-CimMethod -InputObject $encrypted -MethodName GetConversionStatus -ErrorAction Stop
  $protection=Invoke-CimMethod -InputObject $encrypted -MethodName GetProtectionStatus -ErrorAction Stop
  if($locked.ReturnValue -ne 0 -or $conversion.ReturnValue -ne 0 -or $protection.ReturnValue -ne 0){return 'Unknown'}
  if($locked.LockStatus -eq 1){return 'Locked'}
  if($conversion.ConversionStatus -eq 0){return 'Unencrypted'}
  if($protection.ProtectionStatus -eq 1){return 'Protected'}
  return 'Suspended'
 } catch {return 'Unknown'}
}
""";
}
