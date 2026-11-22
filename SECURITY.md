# Security

ThemeLens captures the screen. Please treat anything that could make screen content leave the process, or
that could let the lens block or redirect input, as a security issue.

Until a dedicated contact address exists, report vulnerabilities through GitHub's private vulnerability
reporting on this repository ("Security" tab, "Report a vulnerability"). Please do not open a public issue.

Design constraints that reports can be checked against:

- No frames on disk, over the network or in logs (NFR-02).
- No telemetry; the only network call is the update check, which can be turned off (NFR-03).
- No input hooks and no injection into other processes.
- Theme files are data only and cannot run code.
