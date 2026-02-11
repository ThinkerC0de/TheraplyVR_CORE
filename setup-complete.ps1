# ============================================
# Theraply VR Framework - Complete Setup Script
# ============================================

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Theraply VR Framework - Auto Setup" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Set base path
$basePath = "C:\Users\licen\Projects\theraply-vr-framework"

Write-Host "[1/5] Creating directory structure..." -ForegroundColor Yellow

# Create all directories
$directories = @(
    "$basePath",
    "$basePath\docs",
    "$basePath\docs\API-Reference",
    "$basePath\unity-quest-template",
    "$basePath\unity-quest-template\Assets",
    "$basePath\unity-quest-template\Assets\_TheraplyCore",
    "$basePath\unity-quest-template\Assets\_TheraplyCore\Games",
    "$basePath\unity-quest-template\Assets\_TheraplyCore\Network",
    "$basePath\unity-quest-template\Assets\_TheraplyCore\Network\Discovery",
    "$basePath\unity-quest-template\Assets\_TheraplyCore\Network\Connection",
    "$basePath\unity-quest-template\Assets\_TheraplyCore\Session",
    "$basePath\unity-quest-template\Assets\_TheraplyCore\Firebase",
    "$basePath\unity-quest-template\Assets\_TheraplyCore\Streaming",
    "$basePath\unity-quest-template\Assets\_TheraplyCore\Lifecycle",
    "$basePath\unity-quest-template\Assets\_TheraplyCore\Logging",
    "$basePath\unity-quest-template\Assets\_Examples",
    "$basePath\unity-quest-template\Assets\_Examples\SimpleCubeGame",
    "$basePath\unity-quest-template\Assets\_Examples\SimpleCubeGame\Scripts",
    "$basePath\unity-quest-template\Assets\_Examples\SimpleCubeGame\Scenes",
    "$basePath\unity-quest-template\Assets\_Examples\SimpleCubeGame\Prefabs",
    "$basePath\unity-quest-template\Assets\_Examples\SimpleCubeGame\ScriptableObjects",
    "$basePath\unity-quest-template\Assets\_YourGames",
    "$basePath\unity-quest-template\Packages",
    "$basePath\flutter-controller",
    "$basePath\flutter-controller\lib",
    "$basePath\flutter-controller\lib\core",
    "$basePath\flutter-controller\lib\features",
    "$basePath\firebase",
    "$basePath\firebase\functions",
    "$basePath\firebase\functions\src",
    "$basePath\shared",
    "$basePath\shared\protocol",
    "$basePath\shared\data-models"
)

foreach ($dir in $directories) {
    if (!(Test-Path $dir)) {
        New-Item -ItemType Directory -Path $dir -Force | Out-Null
        Write-Host "  Created: $dir" -ForegroundColor Gray
    }
}

Write-Host "  ✓ Directory structure created!" -ForegroundColor Green
Write-Host ""

Write-Host "[2/5] Downloading files from Claude..." -ForegroundColor Yellow
Write-Host "  Please download these 13 files manually:" -ForegroundColor White
Write-Host "  1. README.md → $basePath\" -ForegroundColor Gray
Write-Host "  2. CHANGELOG.md → $basePath\" -ForegroundColor Gray
Write-Host "  3. LICENSE → $basePath\" -ForegroundColor Gray
Write-Host "  4. SESSION_SUMMARY.md → $basePath\" -ForegroundColor Gray
Write-Host "  5. .gitignore → $basePath\" -ForegroundColor Gray
Write-Host "  6. 03-Creating-Games.md → $basePath\docs\" -ForegroundColor Gray
Write-Host "  7. IGameModule.cs → unity-quest-template\Assets\_TheraplyCore\Games\" -ForegroundColor Gray
Write-Host "  8. BaseGame.cs → unity-quest-template\Assets\_TheraplyCore\Games\" -ForegroundColor Gray
Write-Host "  9. NetworkCommand.cs → unity-quest-template\Assets\_TheraplyCore\Network\" -ForegroundColor Gray
Write-Host "  10. UDPDiscoveryService.cs → unity-quest-template\Assets\_TheraplyCore\Network\Discovery\" -ForegroundColor Gray
Write-Host "  11. TCPConnectionService.cs → unity-quest-template\Assets\_TheraplyCore\Network\Connection\" -ForegroundColor Gray
Write-Host "  12. SimpleCubeGame.cs → unity-quest-template\Assets\_Examples\SimpleCubeGame\Scripts\" -ForegroundColor Gray
Write-Host "  13. SimpleCubeGame README → unity-quest-template\Assets\_Examples\SimpleCubeGame\" -ForegroundColor Gray
Write-Host "  14. YourGames README → unity-quest-template\Assets\_YourGames\" -ForegroundColor Gray
Write-Host ""
Write-Host "  Files are available above in Claude's response ↑" -ForegroundColor Cyan
Write-Host ""

$continue = Read-Host "Press ENTER when you've copied all files"

Write-Host ""
Write-Host "[3/5] Initializing Git repository..." -ForegroundColor Yellow

Set-Location $basePath

# Check if git is installed
try {
    git --version | Out-Null
} catch {
    Write-Host "  ✗ ERROR: Git is not installed!" -ForegroundColor Red
    Write-Host "  Install from: https://git-scm.com/download/win" -ForegroundColor Yellow
    exit 1
}

# Initialize git if not already
if (!(Test-Path ".git")) {
    git init
    Write-Host "  ✓ Git repository initialized" -ForegroundColor Green
} else {
    Write-Host "  ℹ Git repository already exists" -ForegroundColor Yellow
}

# Configure git user if not set
$gitName = git config user.name
if (!$gitName) {
    $userName = Read-Host "Enter your Git username"
    git config --global user.name $userName
}

$gitEmail = git config user.email
if (!$gitEmail) {
    $userEmail = Read-Host "Enter your Git email"
    git config --global user.email $userEmail
}

Write-Host ""
Write-Host "[4/5] Creating initial commit..." -ForegroundColor Yellow

git add .
git commit -m "Initial commit: Theraply VR Framework v0.1.0

- Core Game API (IGameModule, BaseGame)
- Network Layer (UDP Discovery, TCP Connection)
- NetworkCommand ScriptableObject system
- SimpleCubeGame example
- Complete documentation
- ~2,850 lines of code"

Write-Host "  ✓ Initial commit created" -ForegroundColor Green
Write-Host ""

Write-Host "[5/5] Setting up GitHub remote..." -ForegroundColor Yellow
Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "NEXT STEPS:" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "1. Create a NEW repository on GitHub:" -ForegroundColor White
Write-Host "   → https://github.com/new" -ForegroundColor Cyan
Write-Host ""
Write-Host "2. Repository settings:" -ForegroundColor White
Write-Host "   • Name: theraply-vr-framework" -ForegroundColor Gray
Write-Host "   • Description: VR therapy framework for Quest 3" -ForegroundColor Gray
Write-Host "   • Visibility: PRIVATE (nie firmowe!)" -ForegroundColor Yellow
Write-Host "   • DO NOT check 'Initialize with README'" -ForegroundColor Red
Write-Host ""
Write-Host "3. Click 'Create repository'" -ForegroundColor White
Write-Host ""
Write-Host "4. Copy the repository URL (something like):" -ForegroundColor White
Write-Host "   https://github.com/YOUR-USERNAME/theraply-vr-framework.git" -ForegroundColor Gray
Write-Host ""

$repoUrl = Read-Host "Paste your GitHub repository URL here"

if ($repoUrl) {
    Write-Host ""
    Write-Host "Adding remote and pushing..." -ForegroundColor Yellow
    
    try {
        git remote add origin $repoUrl 2>$null
    } catch {
        git remote set-url origin $repoUrl
    }
    
    git branch -M main
    git push -u origin main
    
    if ($LASTEXITCODE -eq 0) {
        Write-Host ""
        Write-Host "========================================" -ForegroundColor Green
        Write-Host "✓ SUCCESS!" -ForegroundColor Green
        Write-Host "========================================" -ForegroundColor Green
        Write-Host ""
        Write-Host "Your project is now on GitHub!" -ForegroundColor White
        Write-Host "URL: $repoUrl" -ForegroundColor Cyan
        Write-Host ""
        Write-Host "Next steps:" -ForegroundColor White
        Write-Host "  1. Open Unity Hub" -ForegroundColor Gray
        Write-Host "  2. Add project: $basePath\unity-quest-template" -ForegroundColor Gray
        Write-Host "  3. Install VContainer via Package Manager" -ForegroundColor Gray
        Write-Host "  4. Install MessagePack-CSharp via Package Manager" -ForegroundColor Gray
        Write-Host "  5. Start building your games!" -ForegroundColor Gray
        Write-Host ""
    } else {
        Write-Host ""
        Write-Host "========================================" -ForegroundColor Red
        Write-Host "✗ ERROR PUSHING TO GITHUB" -ForegroundColor Red
        Write-Host "========================================" -ForegroundColor Red
        Write-Host ""
        Write-Host "This might be because:" -ForegroundColor Yellow
        Write-Host "  1. Repository doesn't exist on GitHub yet" -ForegroundColor Gray
        Write-Host "  2. Wrong URL format" -ForegroundColor Gray
        Write-Host "  3. Authentication failed" -ForegroundColor Gray
        Write-Host ""
        Write-Host "Try manually:" -ForegroundColor White
        Write-Host "  cd $basePath" -ForegroundColor Gray
        Write-Host "  git push -u origin main" -ForegroundColor Gray
        Write-Host ""
    }
} else {
    Write-Host ""
    Write-Host "No URL provided. You can push later with:" -ForegroundColor Yellow
    Write-Host "  cd $basePath" -ForegroundColor Gray
    Write-Host "  git remote add origin YOUR_URL" -ForegroundColor Gray
    Write-Host "  git branch -M main" -ForegroundColor Gray
    Write-Host "  git push -u origin main" -ForegroundColor Gray
}

Write-Host ""
Write-Host "Project location: $basePath" -ForegroundColor Cyan
Write-Host ""

Read-Host "Press ENTER to finish"
