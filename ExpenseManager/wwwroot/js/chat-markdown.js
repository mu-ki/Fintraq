/**
 * Lightweight Markdown → HTML for AI chat replies (headings, bold, tables, lists).
 * Escapes HTML first, then applies safe formatting.
 */
(function () {
    function escapeHtml(text) {
        return String(text)
            .replace(/&/g, "&amp;")
            .replace(/</g, "&lt;")
            .replace(/>/g, "&gt;")
            .replace(/"/g, "&quot;");
    }

    function formatInline(text) {
        let html = escapeHtml(text);
        html = html.replace(/\*\*(.+?)\*\*/g, "<strong>$1</strong>");
        html = html.replace(/__(.+?)__/g, "<strong>$1</strong>");
        html = html.replace(/(?<!\*)\*(?!\*)(.+?)(?<!\*)\*(?!\*)/g, "<em>$1</em>");
        return html;
    }

    function isTableSeparator(line) {
        return /^\s*\|?[\s:\-|]+\|?\s*$/.test(line) && line.includes("-");
    }

    function parseTableCells(line) {
        const parts = line.split("|").map((c) => c.trim());
        if (parts.length && parts[0] === "") parts.shift();
        if (parts.length && parts[parts.length - 1] === "") parts.pop();
        return parts;
    }

    function renderTable(headerLine, bodyLines) {
        const headers = parseTableCells(headerLine);
        const rows = bodyLines.map(parseTableCells).filter((r) => r.length > 0);
        let html = '<table class="chat-md-table"><thead><tr>';
        headers.forEach((h) => {
            html += `<th>${formatInline(h)}</th>`;
        });
        html += "</tr></thead><tbody>";
        rows.forEach((row) => {
            html += "<tr>";
            for (let i = 0; i < headers.length; i++) {
                html += `<td>${formatInline(row[i] || "")}</td>`;
            }
            html += "</tr>";
        });
        html += "</tbody></table>";
        return html;
    }

    function formatChatMarkdown(text) {
        if (!text) return "";

        const normalized = String(text)
            .replace(/\\(\*|_|#|-)/g, "$1");

        const lines = normalized.replace(/\r\n/g, "\n").split("\n");
        const blocks = [];
        let i = 0;

        while (i < lines.length) {
            const line = lines[i];
            const trimmed = line.trim();

            if (trimmed === "") {
                i++;
                continue;
            }

            // Markdown table
            if (trimmed.includes("|") && i + 1 < lines.length && isTableSeparator(lines[i + 1])) {
                const body = [];
                i += 2;
                while (i < lines.length && lines[i].trim().includes("|")) {
                    body.push(lines[i]);
                    i++;
                }
                blocks.push(renderTable(line, body));
                continue;
            }

            // Headings
            const h3 = trimmed.match(/^###\s+(.+)$/);
            if (h3) {
                blocks.push(`<h4 class="chat-md-heading">${formatInline(h3[1])}</h4>`);
                i++;
                continue;
            }
            const h2 = trimmed.match(/^##\s+(.+)$/);
            if (h2) {
                blocks.push(`<h4 class="chat-md-heading">${formatInline(h2[1])}</h4>`);
                i++;
                continue;
            }
            const h1 = trimmed.match(/^#\s+(.+)$/);
            if (h1) {
                blocks.push(`<h4 class="chat-md-heading">${formatInline(h1[1])}</h4>`);
                i++;
                continue;
            }

            // Bullet list
            if (/^[-*•]\s+/.test(trimmed)) {
                const items = [];
                while (i < lines.length && /^[-*•]\s+/.test(lines[i].trim())) {
                    items.push(lines[i].trim().replace(/^[-*•]\s+/, ""));
                    i++;
                }
                blocks.push(
                    "<ul class=\"chat-md-list\">" +
                    items.map((item) => `<li>${formatInline(item)}</li>`).join("") +
                    "</ul>"
                );
                continue;
            }

            // Paragraph (collect consecutive non-special lines)
            const para = [];
            while (i < lines.length) {
                const t = lines[i].trim();
                if (t === "" || t.includes("|") || /^#{1,3}\s/.test(t) || /^[-*•]\s+/.test(t)) {
                    break;
                }
                para.push(lines[i]);
                i++;
            }
            if (para.length > 0) {
                blocks.push(`<p class="chat-md-p">${formatInline(para.join(" "))}</p>`);
            }
        }

        return blocks.join("");
    }

    window.formatChatMarkdown = formatChatMarkdown;
})();
