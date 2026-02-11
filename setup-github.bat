@echo off
REM ============================================
REM Theraply VR Framework - GitHub Setup Script
REM ============================================

echo.
echo ========================================
echo Theraply VR Framework - GitHub Setup
echo ========================================
echo.

REM Check if git is installed
git --version >nul 2>&1
if %errorlevel% neq 0 (
    echo ERROR: Git is not installed!
    echo Please install Git from: https://git-scm.com/download/win
    pause
    exit /b 1
)

echo [1/6] Checking current directory...
echo Current directory: %CD%
echo.

REM Check if we're in the right directory
if not exist "README.md" (
    echo ERROR: README.md not found!
    echo Please run this script from the theraply-vr-framework directory
    pause
    exit /b 1
)

echo [2/6] Configuring Git user...
git config user.name
if %errorlevel% neq 0 (
    set /p GIT_NAME="Enter your Git username: "
    git config --global user.name "%GIT_NAME%"
)

git config user.email
if %errorlevel% neq 0 (
    set /p GIT_EMAIL="Enter your Git email: "
    git config --global user.email "%GIT_EMAIL%"
)

echo.
echo [3/6] Initializing Git repository...
if exist ".git" (
    echo Git repository already exists, skipping init...
) else (
    git init
    echo Git repository initialized!
)

echo.
echo [4/6] Adding files to Git...
git add .
echo Files added!

echo.
echo [5/6] Creating initial commit...
git commit -m "Initial commit: Theraply VR Framework v0.1.0

- Core Game API (IGameModule, BaseGame)
- Network Layer (UDP Discovery, TCP Connection)
- NetworkCommand ScriptableObject system
- SimpleCubeGame example
- Complete documentation
- ~2,850 lines of code"

if %errorlevel% neq 0 (
    echo Commit created!
) else (
    echo Commit already exists or error occurred
)

echo.
echo [6/6] Setting up remote...
echo.
echo ========================================
echo NEXT STEPS:
echo ========================================
echo.
echo 1. Go to GitHub: https://github.com/new
echo 2. Create new repository:
echo    - Name: theraply-vr-framework
echo    - Description: VR therapy framework for Quest 3
echo    - Public or Private: YOUR CHOICE
echo    - DO NOT initialize with README
echo.
echo 3. Copy the repository URL (looks like):
echo    https://github.com/YOUR-USERNAME/theraply-vr-framework.git
echo.

set /p REPO_URL="Paste your GitHub repository URL here: "

if "%REPO_URL%"=="" (
    echo No URL provided. You can add it later with:
    echo git remote add origin YOUR_URL
    echo git branch -M main
    echo git push -u origin main
    pause
    exit /b 0
)

echo.
echo Adding remote origin...
git remote add origin %REPO_URL%
if %errorlevel% neq 0 (
    echo Remote already exists, updating...
    git remote set-url origin %REPO_URL%
)

echo.
echo Renaming branch to main...
git branch -M main

echo.
echo Pushing to GitHub...
git push -u origin main

if %errorlevel% equ 0 (
    echo.
    echo ========================================
    echo SUCCESS! ✓
    echo ========================================
    echo.
    echo Your project is now on GitHub!
    echo URL: %REPO_URL%
    echo.
    echo Next steps:
    echo - Open Unity project in unity-quest-template/
    echo - Install VContainer via Package Manager
    echo - Install MessagePack via Package Manager
    echo - Start building your games!
    echo.
) else (
    echo.
    echo ========================================
    echo ERROR PUSHING TO GITHUB
    echo ========================================
    echo.
    echo This might be because:
    echo 1. Repository doesn't exist on GitHub
    echo 2. Wrong URL
    echo 3. Authentication failed
    echo.
    echo Try again with:
    echo git push -u origin main
    echo.
)

pause
