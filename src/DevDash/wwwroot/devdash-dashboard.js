/* init */

let runnableApplications = {};

document.addEventListener("DevDashLoaded", () => {

    runnableApplications = buildRunnableApplications();

    connectToSseStream();
});

/* exposed functions */

const clearLogs = (applicationId) => {
    var runnableApplication = runnableApplications[applicationId];

    if (!runnableApplication) {
        return;
    }

    runnableApplication.consoleOutputElement.innerHTML = "";
}

const copyOutput = applicationId => {
    var runnableApplication = runnableApplications[applicationId];

    if (!runnableApplication) {
        return;
    }

    const textContent = runnableApplication.consoleOutputElement.innerText;

    navigator.clipboard.writeText(textContent)
        .then(() => {
            showMessage(`${applicationId} output copied.`, "success");
        });
}

const expandOrCollapseRunnableApplication = applicationId => {
    var runnableApplication = runnableApplications[applicationId];

    if (!runnableApplication) {
        return;
    }

    runnableApplication.container.classList.toggle("modal-dialog");
    modalBackground.classList.toggle("active");

    runnableApplication.consoleContainerElement.scrollTop = runnableApplication.consoleContainerElement.scrollHeight;
};

/* helpers */

const connectToSseStream = () => {
    const eventSource = new EventSource("/devdash/sse");

    addEventListeners(eventSource);
};

const addEventListeners = (eventSource) => {
    eventSource.addEventListener("error", (e) => {
        console.error("SSE error:", e);
        showMessage("Error connecting to event stream. Attempting to reconnect...", "error");
    });

    eventSource.addEventListener(EVENT_NAMES_RUNNABLE_APPLICATIONS_STARTED, () => {
        for (const applicationId in runnableApplications) {
            runnableApplications[applicationId].consoleOutputElement.innerHTML = "";
        }
    });

    eventSource.addEventListener(EVENT_NAMES_RUNNABLE_APPLICATION_UPDATED, (e) => {
        const data = JSON.parse(e.data);
        const runnableApplication = runnableApplications[data.application.id];

        if (!runnableApplication || !data || !data.application) {
            return;
        }

        runnableApplication.buttonContainerElement.querySelectorAll(".discovered-url").forEach(el => el.remove());

        if (data.application.running) {
            runnableApplication.startButtonElement.classList.add("disabled");
            runnableApplication.stopButtonElement.classList.remove("disabled");
            runnableApplication.restartButtonElement.classList.remove("disabled");

            data.application.urls.forEach(url => {
                const link = document.createElement("a");

                link.href = url
                link.textContent = "open_in_new";
                link.classList.add("discovered-url");
                link.classList.add("material-symbols-outlined");
                link.target = "_blank";
                link.setAttribute("title", `Open ${url} in new tab or window`);

                runnableApplication.buttonContainerElement.prepend(link);
            });
        }
        else {
            runnableApplication.startButtonElement.classList.remove("disabled");
            runnableApplication.stopButtonElement.classList.add("disabled");
            runnableApplication.restartButtonElement.classList.add("disabled");
        }

        if (data.isBackgroundUpdate) {
            return;
        }

        showMessage(`${data.application.id} is ${data.application.running ? "running" : "stopped"}`);
    });

    eventSource.addEventListener(EVENT_NAMES_APPLICATION_OUTPUT_LINE_EMITTED, (e) => {
        logToConsoleOutput(e, false);
    });

    eventSource.addEventListener(EVENT_NAMES_APPLICATION_ERROR_OUTPUT_LINE_EMITTED, (e) => {
        logToConsoleOutput(e, true);
    });
}

const logToConsoleOutput = (e, isError = false) => {
    const data = JSON.parse(e.data);
    const runnableApplication = runnableApplications[data.id];

    if (!runnableApplication) {
        console.warn(`Received output for unknown application with ID '${data.id}'.`);
        return;
    }

    const isScrolledToBottom = runnableApplication.consoleContainerElement.scrollHeight - runnableApplication.consoleContainerElement.scrollTop
        <= runnableApplication.consoleContainerElement.clientHeight + 5;

    const lineElement = document.createElement("div");
    lineElement.className = isError ? "log-line error" : "log-line";
    lineElement.innerHTML = data.line;
    runnableApplication.consoleOutputElement.appendChild(lineElement);

    if (isScrolledToBottom) {
        runnableApplication.consoleContainerElement.scrollTop = runnableApplication.consoleContainerElement.scrollHeight;
    }
}

const buildRunnableApplications = () => {
    const output = {};

    const runnableApplicationElements = document.querySelectorAll("div.runnable-application");

    runnableApplicationElements.forEach(el => {
        output[el.dataset.applicationId] = {
            container: el,
            consoleContainerElement: el.querySelector(".runnable-application-console-output"),
            consoleOutputElement: el.querySelector(".runnable-application-console-output > pre > code"),
            buttonContainerElement: el.querySelector(".runnable-application-header-buttons"),
            startButtonElement: el.querySelector(".runnable-application-header-buttons .start"),
            stopButtonElement: el.querySelector(".runnable-application-header-buttons .stop"),
            restartButtonElement: el.querySelector(".runnable-application-header-buttons .restart")
        };
    });

    return output;
};  