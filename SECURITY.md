# Security Policy

Report suspected vulnerabilities privately to **bomtobe.sw@gmail.com**. Please include the affected version, reproduction steps, and the expected security impact. Do not post credentials, personal files or exploit details in a public issue while a report is being assessed.

보안 취약점이 의심되면 **bomtobe.sw@gmail.com**으로 비공개 신고해 주세요. 영향을 받는 버전, 재현 방법과 예상되는 보안 영향을 포함해 주세요. 검토 중에는 인증 정보, 개인 파일 또는 구체적인 악용 방법을 공개 이슈에 게시하지 마세요.

Glasscape runs with the current user's permissions. It does not require administrator privileges. Imported media is passed to the bundled player as a literal process argument, and user mpv configuration and scripts are disabled. Native dependency downloads are pinned to SHA-256 hashes. Keep Windows and Glasscape updated and use media files from sources you trust.

The repository excludes local credentials, signing keys, build outputs, internal work records and non-distributed videos. Release preparation includes secret scanning and manual review. These checks reduce risk but are not a guarantee that every vulnerability has been found.
