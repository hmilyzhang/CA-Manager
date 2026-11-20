# ELEVATED: template ACL + OCSP revocation configuration
$ErrorActionPreference = 'Continue'

# 1) Template ACL: grant LAB-DC-01$ Read + Enroll on OCSP Response Signing
$tplPath = "LDAP://CN=OCSPResponseSigning,CN=Certificate Templates,CN=Public Key Services,CN=Services,$((New-Object System.DirectoryServices.DirectoryEntry('LDAP://RootDSE')).properties['configurationNamingContext'][0])"
$adEntry = New-Object System.DirectoryServices.DirectoryEntry($tplPath)
$adEntry.Options.SecurityMasks = [System.DirectoryServices.SecurityMasks]::Dacl
$adEntry.RefreshCache()
$sid = (New-Object System.Security.Principal.NTAccount("HOME", "LAB-DC-01$")).Translate([System.Security.Principal.SecurityIdentifier])
# Enroll extended right
$adEntry.ObjectSecurity.AddAccessRule((New-Object System.DirectoryServices.ActiveDirectoryAccessRule($sid, [System.DirectoryServices.ActiveDirectoryRights]::ExtendedRight, [System.DirectoryServices.ActiveDirectoryAccessControlType]::Allow, [Guid]"0e10c968-2fb8-11d2-90d4-00c04f79dc55")))
# Read
$adEntry.ObjectSecurity.AddAccessRule((New-Object System.DirectoryServices.ActiveDirectoryAccessRule($sid, [System.DirectoryServices.ActiveDirectoryRights]::GenericRead, [System.DirectoryServices.ActiveDirectoryAccessControlType]::Allow)))
$adEntry.CommitChanges()
Write-Host "template ACL committed"

# 2) OCSP revocation configuration via certadm.OCSPAdmin COM
$admin = New-Object -ComObject "certadm.OCSPAdmin"
$cfg = $admin.GetConfiguration("lab-dc-01", $true)
try { $cfg.SetCAConfig("lab-dc-01"); Write-Host "CAConfig set" } catch { Write-Host "SetCAConfig: $($_.Exception.Message)" }
try { $admin.SetConfiguration($cfg, $true); Write-Host "config committed" } catch { Write-Host "commit: $($_.Exception.Message)" }

# 3) ensure responder service is running
Get-Service "OCSPResponder" -ErrorAction SilentlyContinue | Start-Service -ErrorAction Continue
Write-Host "OCSPResponder service state: $((Get-Service OCSPResponder -ErrorAction SilentlyContinue).Status)"
