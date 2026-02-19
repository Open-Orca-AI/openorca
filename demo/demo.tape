# VHS tape for recording OpenOrca demo GIF
# https://github.com/charmbracelet/vhs

Output demo/demo.gif
Set FontSize 14
Set Width 1200
Set Height 700
Set Theme "Catppuccin Mocha"
Set TypingSpeed 50ms
Set Shell bash

# Launch OpenOrca
Type "openorca"
Enter
Sleep 3s

# Show slash commands
Type "/help"
Enter
Sleep 3s

# Show context usage
Type "/context"
Enter
Sleep 2s

# Ask a real question — triggers agent loop with tool calls
Type "Read the README.md and give me a summary of this project"
Enter
Sleep 18s

# Exit
Type "/exit"
Enter
Sleep 1s
