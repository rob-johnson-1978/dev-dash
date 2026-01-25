/* init */

let messageContainer = undefined;
let modalBackground = undefined;

addEventListener("DOMContentLoaded", () => {
    messageContainer = document.querySelector("#message_container");

    modalBackground = document.querySelector("#modal_background");

    modalBackground.addEventListener("click", closeAllModals);

    document.addEventListener("keydown", (event) => {
        if (event.key === "Escape") {
            closeAllModals();
        }
    });

    const event = new Event("DevDashLoaded");
    document.dispatchEvent(event);
});

/* helpers */

const showMessage = (message, status = "default") => {
    const messageElement = document.createElement("div");

    messageElement.className = "message";
    messageElement.classList.add(status);

    messageElement.innerHTML = message;

    messageContainer.appendChild(messageElement);

    setTimeout(() => {
        messageElement.remove();
    }, 10000);
}

const closeAllModals = () => {
    document.querySelectorAll(".modal-dialog").forEach(el => el.classList.remove("modal-dialog"));
    modalBackground.classList.remove("active");
};