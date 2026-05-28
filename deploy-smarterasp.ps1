# EventEase - SmarterASP.NET Deployment Automation Script
# This script compiles the application locally and provides options to deploy via FTP or a Git Release Branch.

param (
    [Parameter(Mandatory=$false)]
    [string]$Method = "prompt" # options: ftp, git, prompt
)

Write-Host "=============================================" -ForegroundColor Cyan
Write-Host "    EventEase SmarterASP.NET Deployment Tool" -ForegroundColor Cyan
Write-Host "=============================================" -ForegroundColor Cyan

# Ensure we are in the root directory containing EventEase.sln
if (-not (Test-Path "EventEase.sln")) {
    Write-Error "EventEase.sln not found. Please run this script from the repository root."
    Exit
}

# Step 1: Run local publish
Write-Host "`n[1/3] Compiling and publishing project locally..." -ForegroundColor Yellow
$PublishDir = Join-Path (Get-Location) "publish"

if (Test-Path $PublishDir) {
    Remove-Item -Path $PublishDir -Recurse -Force -ErrorAction SilentlyContinue
}

dotnet publish EventEase/EventEase.Api.csproj -c Release -o $PublishDir

if ($LASTEXITCODE -ne 0) {
    Write-Error "Failed to compile/publish the project."
    Exit
}

Write-Host "Successfully compiled to: $PublishDir" -ForegroundColor Green

# Step 2: Choose Deployment Method
$choice = $Method
if ($choice -eq "prompt") {
    Write-Host "`nChoose your deployment method:" -ForegroundColor Cyan
    Write-Host "1) Upload via FTP (Recommended & Easiest for SmarterASP.NET)"
    Write-Host "2) Deploy via Git Release Branch (Commit precompiled files to a 'release' branch)"
    
    $input = Read-Host "Select option (1 or 2)"
    if ($input -eq "1") {
        $choice = "ftp"
    } else {
        $choice = "git"
    }
}

# Step 3: Execute Deployment
if ($choice -eq "ftp") {
    Write-Host "`n[2/3] Collecting FTP Credentials..." -ForegroundColor Yellow
    $FtpServer = Read-Host "FTP Server (e.g., ftp.smarterasp.net or your site IP)"
    $FtpUser = Read-Host "FTP Username"
    $FtpPassword = Read-Host -AsSecureString "FTP Password"
    
    # Decrypt password for WebClient
    $BSTR = [System.Runtime.InteropServices.Marshal]::SecureStringToBSTR($FtpPassword)
    $PlainPassword = [System.Runtime.InteropServices.Marshal]::PtrToStringAuto($BSTR)
    
    # Clean server URL
    if ($FtpServer -notlike "ftp://*") {
        $FtpServer = "ftp://$FtpServer"
    }
    
    Write-Host "`n[3/3] Uploading files to FTP server..." -ForegroundColor Yellow
    
    # Helper function to upload directories recursively
    function Upload-Directory($localPath, $ftpUrl) {
        $webClient = New-Object System.Net.WebClient
        $webClient.Credentials = New-Object System.Net.NetworkCredential($FtpUser, $PlainPassword)
        
        # Create directory on FTP if not exists
        try {
            $req = [System.Net.FtpWebRequest]::Create($ftpUrl)
            $req.Method = [System.Net.WebRequestMethods+Ftp]::MakeDirectory
            $req.Credentials = New-Object System.Net.NetworkCredential($FtpUser, $PlainPassword)
            $resp = $req.GetResponse()
            $resp.Close()
        } catch {
            # Directory might already exist, ignore error
        }
        
        $files = Get-ChildItem -Path $localPath
        foreach ($file in $files) {
            $targetUrl = "$ftpUrl/" + $file.Name
            if ($file.PSIsContainer) {
                # Recursive upload for directories
                Upload-Directory $file.FullName $targetUrl
            } else {
                # Upload file
                Write-Host "Uploading: $($file.Name)..."
                try {
                    $webClient.UploadFile($targetUrl, $file.FullName)
                } catch {
                    Write-Warning "Failed to upload: $($file.Name). Error: $_"
                }
            }
        }
    }
    
    Upload-Directory $PublishDir $FtpServer
    Write-Host "`nDeployment Completed successfully!" -ForegroundColor Green
    
} elseif ($choice -eq "git") {
    Write-Host "`n[2/3] Creating a precompiled release branch..." -ForegroundColor Yellow
    
    # We want to push the contents of the publish folder directly.
    # To do this cleanly, we initialize a temporary repo in the publish folder and push it.
    $GitUrl = Read-Host "Enter your Git Repository URL (e.g., your GitHub/Railway/SmarterASP Git URL)"
    $Branch = Read-Host "Enter deployment branch name (default: release)"
    if ([string]::IsNullOrEmpty($Branch)) { $Branch = "release" }
    
    Push-Location $PublishDir
    try {
        # Initialize temp git
        git init -b $Branch
        git add .
        git commit -m "Deploy: Precompiled release build"
        git remote add origin $GitUrl
        Write-Host "Pushing to $GitUrl [$Branch]..." -ForegroundColor Yellow
        git push origin $Branch --force
        Write-Host "`nDeployment Completed! Pushed build files to remote branch: $Branch" -ForegroundColor Green
    } catch {
        Write-Error "Failed to push to git repository."
    } finally {
        Pop-Location
    }
}
