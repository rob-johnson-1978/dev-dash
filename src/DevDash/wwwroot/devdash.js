/* init */

let messageContainer = undefined;
let modalBackground = undefined;

addEventListener("DOMContentLoaded", () => {
    messageContainer = document.querySelector("#message_container");

    modalBackground = document.querySelector("#modal_background");

    modalBackground.addEventListener("click", closeAllModals);
    document.addEventListener("keydown", closeAllModals);


    const event = new Event("DevDashLoaded");
    document.dispatchEvent(event);
});

/* exposed functions */

const sendCommand = (applicationId, command) => {
    fetch(`/devdash/command/${command}/${applicationId}`, {
        method: "POST"
    });
}

/* helpers */

const showMessage = (message, status = "default") => {
    const messageElement = document.createElement("div");

    messageElement.className = "message";
    messageElement.classList.add(status);

    messageElement.textContent = message;

    messageContainer.appendChild(messageElement);

    setTimeout(() => {
        messageElement.remove();
    }, 5000);
}

const closeAllModals = () => {
    document.querySelectorAll(".modal-dialog").forEach(el => el.classList.remove("modal-dialog"));
    modalBackground.classList.remove("active");
};