/* init */

let runnableProcesses = {};

const globalStartButton = document.getElementById("global_start");
const globalStopButton = document.getElementById("global_stop");
const globalRestartButton = document.getElementById("global_restart");

document.addEventListener("DevDashLoaded", () => {

    runnableProcesses = buildRunnableProcesses();

    connectToSseStream();
});

/* exposed functions */

const sendDashboardCommand = (command) => {
    fetch(`/devdash/dashboard/${command}`, {
        method: "POST"
    });
}

const sendDashboardProcessCommand = (processId, command) => {
    fetch(`/devdash/dashboard/process/${processId}/${command}`, {
        method: "POST"
    });
}

const disableGlobalButtons = () => {
    globalStartButton.classList.add("disabled");
    globalStopButton.classList.add("disabled");
    globalRestartButton.classList.add("disabled");
};

const disableRunnableProcessButtons = (processId) => {
    var runnableProcess = runnableProcesses[processId];

    if (!runnableProcess) {
        return;
    }

    runnableProcess.startButtonElement.classList.add("disabled");
    runnableProcess.stopButtonElement.classList.add("disabled");
    runnableProcess.restartButtonElement.classList.add("disabled");
};

const clearLogs = (processId) => {

    if (!processId) {
        for (var key in runnableProcesses) {
            runnableProcesses[key].consoleOutputElement.innerHTML = "";
        }

        return;
    }

    var runnableProcess = runnableProcesses[processId];

    if (!runnableProcess) {
        return;
    }

    runnableProcess.consoleOutputElement.innerHTML = "";
}

const copyOutput = processId => {
    var runnableProcess = runnableProcesses[processId];

    if (!runnableProcess) {
        return;
    }

    const textContent = runnableProcess.consoleOutputElement.innerText;

    navigator.clipboard.writeText(textContent)
        .then(() => {
            showMessage(`${processId} output copied.`, "success");
        });
}

const expandOrCollapseRunnableProcess = processId => {
    var runnableProcess = runnableProcesses[processId];

    if (!runnableProcess) {
        return;
    }

    runnableProcess.container.classList.toggle("modal-dialog");
    modalBackground.classList.toggle("active");

    runnableProcess.consoleContainerElement.scrollTop = runnableProcess.consoleContainerElement.scrollHeight;
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

        switch (data.currentBehaviour) {
            case ENUMS_DASHBOARD_BEHAVIOUR_NONE:
                {
                    globalStartButton.classList.add("disabled");
                    globalStopButton.classList.add("disabled");
                    globalRestartButton.classList.add("disabled");
                    break;
                }
            case ENUMS_DASHBOARD_BEHAVIOUR_CONFIGURED:
                {
                    globalStartButton.classList.remove("disabled");
                    globalStopButton.classList.add("disabled");
                    globalRestartButton.classList.add("disabled");
                    break;
                }
            case ENUMS_DASHBOARD_BEHAVIOUR_STARTING:
            case ENUMS_DASHBOARD_BEHAVIOUR_STARTED:
                {
                    globalStartButton.classList.add("disabled");
                    globalStopButton.classList.remove("disabled");
                    globalRestartButton.classList.remove("disabled");
                    break;
                }
        }
    });

    eventSource.addEventListener(EVENT_NAMES_RUNNABLE_PROCESSES_STARTING, () => {
        for (const processId in runnableProcesses) {
            runnableProcesses[processId].consoleOutputElement.innerHTML = "";
        }
    });

    eventSource.addEventListener(EVENT_NAMES_RUNNABLE_PROCESS_STATUS_PUBLISHED, (e) => {
        const data = JSON.parse(e.data);
        const runnableProcess = runnableProcesses[data.processId];

        if (!runnableProcess) {
            return;
        }

        runnableProcess.buttonContainerElement.querySelectorAll(".discovered-url").forEach(el => el.remove());

        switch (data.currentBehaviour) {
            case ENUMS_PROCESS_BEHAVIOUR_NONE:
            case ENUMS_PROCESS_BEHAVIOUR_START_REQUESTED:
                runnableProcess.startButtonElement.classList.add("disabled");
                runnableProcess.stopButtonElement.classList.add("disabled");
                runnableProcess.restartButtonElement.classList.add("disabled");
                break;

            case ENUMS_PROCESS_BEHAVIOUR_STARTED:
                runnableProcess.startButtonElement.classList.add("disabled");
                runnableProcess.stopButtonElement.classList.remove("disabled");
                runnableProcess.restartButtonElement.classList.remove("disabled");

                (data.urls || []).forEach(url => {
                    const link = document.createElement("a");

                    link.href = url;
                    link.textContent = "open_in_new";
                    link.classList.add("discovered-url");
                    link.classList.add("material-symbols-outlined");
                    link.target = "_blank";
                    link.setAttribute("title", `Open ${url} in new tab or window`);

                    runnableProcess.buttonContainerElement.prepend(link);
                });
                break;

            case ENUMS_PROCESS_BEHAVIOUR_STOPPED:
                runnableProcess.startButtonElement.classList.remove("disabled");
                runnableProcess.stopButtonElement.classList.add("disabled");
                runnableProcess.restartButtonElement.classList.add("disabled");
                break;
        }
    });

    eventSource.addEventListener(EVENT_NAMES_PROCESS_OUTPUT_LINE_EMITTED, (e) => {
        addLineToConsoleOutput(e, false);
    });

    eventSource.addEventListener(EVENT_NAMES_PROCESS_ERROR_OUTPUT_LINE_EMITTED, (e) => {
        addLineToConsoleOutput(e, true);
    });

    eventSource.addEventListener(EVENT_NAMES_MESSAGE_AREA_MESSAGE_PUBLISHED, (e) => {
        const data = JSON.parse(e.data);
        showMessage(data.message, data.currentBehaviour);
    });
}

const addLineToConsoleOutput = (e, isError = false) => {
    const data = JSON.parse(e.data);
    const runnableProcess = runnableProcesses[data.id];

    if (!runnableProcess) {
        console.warn(`Received output for unknown process with ID '${data.id}'.`);
        return;
    }

    const consoleOutput = runnableProcess.consoleOutputElement;
    const isScrolledToBottom = runnableProcess.consoleContainerElement.scrollHeight - runnableProcess.consoleContainerElement.scrollTop
        <= runnableProcess.consoleContainerElement.clientHeight + 5;

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
        runnableProcess.consoleContainerElement.scrollTop = runnableProcess.consoleContainerElement.scrollHeight;
    }
}

const buildRunnableProcesses = () => {
    const output = {};

    const runnableProcessElements = document.querySelectorAll("div.runnable-process");

    runnableProcessElements.forEach(el => {
        output[el.dataset.processId] = {
            container: el,
            consoleContainerElement: el.querySelector(".runnable-process-console-output"),
            consoleOutputElement: el.querySelector(".runnable-process-console-output > pre > code"),
            buttonContainerElement: el.querySelector(".runnable-process-header-buttons"),
            startButtonElement: el.querySelector(".runnable-process-header-buttons .start"),
            stopButtonElement: el.querySelector(".runnable-process-header-buttons .stop"),
            restartButtonElement: el.querySelector(".runnable-process-header-buttons .restart")
        };
    });

    return output;
};  