/* init */

let runnableApplications = {};

const globalStartButton = document.getElementById("global_start");
const globalStopButton = document.getElementById("global_stop");
const globalRestartButton = document.getElementById("global_restart");

document.addEventListener("DevDashLoaded", () => {

    runnableApplications = buildRunnableApplications();

    connectToSseStream();
});

/* exposed functions */

const sendDashboardCommand = (command) => {
    fetch(`/devdash/dashboard/${command}`, {
        method: "POST"
    });
}

const sendDashboardApplicationCommand = (applicationId, command) => {
    fetch(`/devdash/dashboard/application/${applicationId}/${command}`, {
        method: "POST"
    });
}

const disableGlobalButtons = () => {
    globalStartButton.classList.add("disabled");    
    globalStopButton.classList.add("disabled");
    globalRestartButton.classList.add("disabled");
};

const disableRunnableApplicationButtons = (applicationId) => {
    var runnableApplication = runnableApplications[applicationId];

    if (!runnableApplication) {
        return;
    }

    runnableApplication.startButtonElement.classList.add("disabled");
    runnableApplication.stopButtonElement.classList.add("disabled");
    runnableApplication.restartButtonElement.classList.add("disabled");
};

const clearLogs = (applicationId) => {

    if (!applicationId) {
        for (var key in runnableApplications) {
            runnableApplications[key].consoleOutputElement.innerHTML = "";
        }

        return;
    }

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
    const eventSource = new EventSource("/devdash/dashboard/event-stream");

    addEventListeners(eventSource);
};

const addEventListeners = (eventSource) => {
    eventSource.addEventListener("error", (e) => {
        console.error("SSE error:", e);
        showMessage("Error connecting to event stream. Attempting to reconnect...", "error");
    });

    eventSource.addEventListener(EVENT_NAMES_DASHBOARD_STATUS_PUBLISHED, (e) => {
        const data = JSON.parse(e.data);

        switch (data.status) {
            case ENUMS_RUNSTATUS_NEVER_STARTED:
            case ENUMS_RUNSTATUS_START_REQUESTED: {
                globalStartButton.classList.add("disabled");
                globalStopButton.classList.add("disabled");
                globalRestartButton.classList.add("disabled");
                break;
            }
            case ENUMS_RUNSTATUS_STARTED: {
                globalStartButton.classList.add("disabled");
                globalStopButton.classList.remove("disabled");
                globalRestartButton.classList.remove("disabled");
                break;
            }
            case ENUMS_RUNSTATUS_STOPPED: {
                globalStartButton.classList.remove("disabled");
                globalStopButton.classList.add("disabled");
                globalRestartButton.classList.add("disabled");
                break;
            }
        }
    });

    eventSource.addEventListener(EVENT_NAMES_RUNNABLE_APPLICATIONS_STARTING, () => {
        for (const applicationId in runnableApplications) {
            runnableApplications[applicationId].consoleOutputElement.innerHTML = "";
        }
    });

    eventSource.addEventListener(EVENT_NAMES_RUNNABLE_APPLICATION_STATUS_PUBLISHED, (e) => {
        const data = JSON.parse(e.data);
        const runnableApplication = runnableApplications[data.applicationId];

        if (!runnableApplication) {
            return;
        }

        runnableApplication.buttonContainerElement.querySelectorAll(".discovered-url").forEach(el => el.remove());

        if (data.status === ENUMS_RUNSTATUS_NEVER_STARTED || data.status === ENUMS_RUNSTATUS_START_REQUESTED) {
            runnableApplication.startButtonElement.classList.add("disabled");
            runnableApplication.stopButtonElement.classList.add("disabled");
            runnableApplication.restartButtonElement.classList.add("disabled");
            return;
        }

        if (data.status === ENUMS_RUNSTATUS_STARTED) {
            runnableApplication.startButtonElement.classList.add("disabled");
            runnableApplication.stopButtonElement.classList.remove("disabled");
            runnableApplication.restartButtonElement.classList.remove("disabled");

            data.urls.forEach(url => {
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
    });

    eventSource.addEventListener(EVENT_NAMES_APPLICATION_OUTPUT_LINE_EMITTED, (e) => {
        addLineToConsoleOutput(e, false);
    });

    eventSource.addEventListener(EVENT_NAMES_APPLICATION_ERROR_OUTPUT_LINE_EMITTED, (e) => {
        addLineToConsoleOutput(e, true);
    });

    eventSource.addEventListener(EVENT_NAMES_MESSAGE_AREA_MESSAGE_PUBLISHED, (e) => {
        const data = JSON.parse(e.data);
        showMessage(data.message, data.status);
    });
}

const addLineToConsoleOutput = (e, isError = false) => {
    const data = JSON.parse(e.data);
    const runnableApplication = runnableApplications[data.id];

    if (!runnableApplication) {
        console.warn(`Received output for unknown application with ID '${data.id}'.`);
        return;
    }

    const consoleOutput = runnableApplication.consoleOutputElement;
    const isScrolledToBottom = runnableApplication.consoleContainerElement.scrollHeight - runnableApplication.consoleContainerElement.scrollTop
        <= runnableApplication.consoleContainerElement.clientHeight + 5;

    const lineElement = document.createElement("div");
    lineElement.className = isError ? "log-line error" : "log-line";
    lineElement.innerHTML = data.line;
    consoleOutput.appendChild(lineElement);

    if (CONFIG_CONSOLE_OUTPUT_MAX_LINES && consoleOutput.childElementCount > window.CONFIG_CONSOLE_OUTPUT_MAX_LINES) {
        const linesToRemove = window.CONFIG_CONSOLE_OUTPUT_LINE_REMOVAL_BATCH_SIZE || 1;

        for (let i = 0; i < linesToRemove && consoleOutput.firstElementChild; i++) {
            consoleOutput.firstElementChild.remove();
        }
    }

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