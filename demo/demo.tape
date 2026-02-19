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
Type "openorca --demo"
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

# Show interactive config editor
Type "/config"
Enter
Sleep 2s

# Enter LM Studio settings (already highlighted as first item)
Enter
Sleep 2s

# Scroll through LM Studio options to show available settings
Down
Sleep 150ms
Down
Sleep 150ms
Down
Sleep 150ms
Down
Sleep 150ms
Down
Sleep 150ms
Down
Sleep 150ms
Down
Sleep 150ms
Down
Sleep 150ms

# Select "Back" to return to main menu
Enter
Sleep 1s

# Navigate to "View All" to display the full config table
Down
Sleep 150ms
Down
Sleep 150ms
Down
Sleep 150ms
Down
Sleep 150ms

# Select "View All"
Enter
Sleep 3s

# Navigate to "Exit (discard changes)"
Down
Sleep 150ms
Down
Sleep 150ms
Down
Sleep 150ms
Down
Sleep 150ms
Down
Sleep 150ms
Down
Sleep 150ms

# Exit config editor
Enter
Sleep 1s

# Ask a real question — triggers agent loop with tool calls
Type "Read the README.md and give me a summary of this project"
Enter
Sleep 18s

# Exit
Type "/exit"
Enter
Sleep 1s
