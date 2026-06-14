(() => {
    const formatUtcElements = () => {
        document.querySelectorAll("[data-utc]").forEach((el) => {
            const raw = el.getAttribute("data-utc");
            if (!raw) {
                return;
            }

            const date = new Date(raw);
            if (Number.isNaN(date.getTime())) {
                return;
            }

            el.textContent = date.toLocaleString(undefined, {
                dateStyle: "short",
                timeStyle: "short"
            });
        });
    };

    if (document.readyState === "loading") {
        document.addEventListener("DOMContentLoaded", formatUtcElements);
    } else {
        formatUtcElements();
    }
})();
