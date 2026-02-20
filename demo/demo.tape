# VHS tape for recording OpenOrca demo GIF
# https://github.com/charmbracelet/vhs
#
# Recording strategy: run ~4x faster than real-time, then use ffmpeg to
# slow the output back to natural playback speed. This cuts CI recording
# time from ~60s to ~15s while producing the same visual result.
# The workflow's ffmpeg step applies the 4x slowdown after VHS finishes.

Output demo/demo-raw.gif
Set FontSize 14
Set Width 1200
Set Height 700
Set Theme "Catppuccin Mocha"
Set Framerate 60
Set TypingSpeed 12ms
Set Shell bash

# Launch OpenOrca
Type "openorca --demo"
Enter
Sleep 800ms

# Show slash commands
Type "/help"
Enter
Sleep 800ms

# Show context usage
Type "/context"
Enter
Sleep 500ms

# Show configuration (renders config table in demo mode)
Type "/config"
Enter
Sleep 800ms

# Ask a real question — triggers agent loop with tool calls
Type "Read the README.md and give me a summary of this project"
Enter
Sleep 5s

# Exit
Type "/exit"
Enter
Sleep 300ms
