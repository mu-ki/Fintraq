(() => {
    const toggle = document.getElementById("fintraqChatToggle");
    const widget = document.getElementById("fintraqChatWidget");
    const closeBtn = document.getElementById("fintraqChatClose");
    const form = document.getElementById("fintraqChatForm");
    const input = document.getElementById("fintraqChatInput");
    const sendBtn = document.getElementById("fintraqChatSend");
    const log = document.getElementById("fintraqChatLog");

    if (!toggle || !widget || !closeBtn || !form || !input || !sendBtn || !log) {
        return;
    }

    let isOpen = false;
    let isPending = false;
    let hasGreeted = false;

    const renderMessage = (text, role) => {
        const bubble = document.createElement("div");
        bubble.className = `fintraq-chat-bubble ${role}`;
        bubble.textContent = text;
        log.appendChild(bubble);
        log.scrollTop = log.scrollHeight;
        return bubble;
    };

    const renderTyping = () => {
        const bubble = document.createElement("div");
        bubble.className = "fintraq-chat-bubble assistant fintraq-chat-typing";
        bubble.innerHTML = "<span></span><span></span><span></span>";
        log.appendChild(bubble);
        log.scrollTop = log.scrollHeight;
        return bubble;
    };

    const setOpen = (open) => {
        isOpen = open;
        widget.classList.toggle("open", isOpen);
        toggle.classList.toggle("active", isOpen);
        widget.setAttribute("aria-hidden", String(!isOpen));
        toggle.setAttribute("aria-expanded", String(isOpen));

        if (isOpen) {
            if (!hasGreeted) {
                renderMessage("Ask balance, income, or expenses by month. You can type short phrases like \"balance\" or \"income\" — I'll use this month if you don't specify.", "assistant");
                hasGreeted = true;
            }
            setTimeout(() => input.focus(), 120);
        }
    };

    const setPending = (pending) => {
        isPending = pending;
        input.disabled = pending;
        sendBtn.disabled = pending;
    };

    const suggestions = document.getElementById("fintraqChatSuggestions");
    if (suggestions) {
        suggestions.querySelectorAll(".fintraq-chat-chip").forEach((btn) => {
            btn.addEventListener("click", function () {
                const prompt = this.getAttribute("data-prompt") || this.textContent || "";
                if (prompt && input && !isPending) {
                    input.value = prompt;
                    form.requestSubmit();
                }
            });
        });
    }

    toggle.addEventListener("click", () => setOpen(!isOpen));
    closeBtn.addEventListener("click", () => setOpen(false));

    document.addEventListener("keydown", (event) => {
        if (event.key === "Escape" && isOpen) {
            setOpen(false);
        }
    });

    form.addEventListener("submit", async (event) => {
        event.preventDefault();
        if (isPending) {
            return;
        }

        const message = input.value.trim();
        if (!message) {
            return;
        }

        renderMessage(message, "user");
        input.value = "";
        setPending(true);
        const typingBubble = renderTyping();

        try {
            const response = await fetch("/api/chat/query", {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify({ message })
            });

            if (!response.ok) {
                renderMessage("I could not process that request right now. Please try again.", "assistant");
                return;
            }

            const data = await response.json();
            renderMessage(data.reply || "I did not get a valid response. Please rephrase.", "assistant");
        } catch {
            renderMessage("Network error while contacting AI service. Please try again.", "assistant");
        } finally {
            typingBubble.remove();
            setPending(false);
            input.focus();
        }
    });
})();
