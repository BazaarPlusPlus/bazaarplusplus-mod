#!/usr/bin/env bash
set -euo pipefail

CYAN='\033[0;36m'
GREEN='\033[0;32m'
RED='\033[0;31m'
RESET='\033[0m'

case "$(uname -s)" in
    MINGW*|MSYS*|CYGWIN*) ;;
    *)
        echo -e "${RED}This script is for Windows (Git Bash) only.${RESET}" >&2
        exit 1
        ;;
esac

echo -e "${CYAN}== Setting up Git Bash on ${GREEN}Windows${CYAN} ==${RESET}"
echo ""

BASHRC="$HOME/.bashrc"

# Prompt
cat >> "$BASHRC" << 'EOF'

# === BazaarPlusPlus Dev Setup ===

__prompt() {
    local CYAN='\[\e[0;36m\]'
    local RED='\[\e[0;31m\]'
    local RESET='\[\e[0m\]'

    local branch
    branch=$(git rev-parse --abbrev-ref HEAD 2>/dev/null)

    local git_part=""
    if [ -n "$branch" ]; then
        local dirty=""
        if [ -n "$(git status --porcelain 2>/dev/null)" ]; then
            dirty=" ${RED}✗"
        fi
        git_part=" ${CYAN}git:(${RED}${branch}${CYAN})${dirty}"
    fi

    PS1="${CYAN}➜  \W${git_part}${RESET} "
}

PROMPT_COMMAND='__prompt'

# Aliases
alias ll='ls -la --color=auto'
alias gs='git status'
alias gp='git push'
alias gl='git log --oneline --graph --decorate -10'

# ssh-agent auto-start
if [ -z "$SSH_AUTH_SOCK" ]; then
    eval $(ssh-agent -s) &>/dev/null
    ssh-add ~/.ssh/id_ed25519 2>/dev/null || true
fi

EOF

# VSCode / Cursor settings
patch_vscode_settings() {
    local settings_file="$1"
    local app_name="$2"

    if [ ! -f "$settings_file" ]; then
        mkdir -p "$(dirname "$settings_file")"
        echo "{}" > "$settings_file"
    fi

    node -e "
const fs = require('fs');
const file = '$settings_file';
const s = JSON.parse(fs.readFileSync(file, 'utf8'));
s['terminal.integrated.defaultProfile.windows'] = 'Git Bash';
s['terminal.integrated.fontFamily'] = 'Cascadia Code';
fs.writeFileSync(file, JSON.stringify(s, null, 2));
console.log('Patched: ' + file);
"
}

APPDATA_UNIX=$(echo "$APPDATA" | sed 's|\\|/|g' | sed 's|^\([A-Za-z]\):|/\L\1|')

for app in "Code" "Cursor"; do
    settings="$APPDATA_UNIX/$app/User/settings.json"
    if [ -d "$APPDATA_UNIX/$app" ]; then
        patch_vscode_settings "$settings" "$app"
    fi
done

echo ""
echo -e "${GREEN}Done!${RESET}"
echo -e "Run ${CYAN}source ~/.bashrc${RESET} to apply changes."
echo ""
echo -e "Note: Install ${GREEN}Cascadia Code${RESET} font for best experience."
