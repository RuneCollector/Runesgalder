const analysisButton = document.querySelector("#run-analysis");
const refusalButton = document.querySelector("#run-refusal");
const doctorLine = document.querySelector("#doctor");
const statusLine = document.querySelector("#status");
const sandboxTime = document.querySelector("#sandbox-time");
const eventList = document.querySelector("#events");
const resultPane = document.querySelector("#result");
const dataPane = document.querySelector("#data");

let stream = null;

function el(tag, className, text) {
  const node = document.createElement(tag);
  if (className) node.className = className;
  if (text !== undefined && text !== null) node.textContent = text;
  return node;
}

function stamp(iso) {
  const date = iso ? new Date(iso) : new Date();
  return date.toLocaleTimeString("en-GB", { hour12: false }) + "." + String(date.getMilliseconds()).padStart(3, "0");
}

function ms(value) {
  return (Math.round(value * 10) / 10).toLocaleString("en-GB") + " ms";
}

function describe(event) {
  switch (event.type) {
    case "run_started":
      return { kind: "run", detail: event.flow, tone: "" };
    case "step_started":
      return { kind: "step", detail: event.step, tone: "" };
    case "step_completed":
      return { kind: "step ok", detail: event.step + " · " + ms(event.ms), tone: "ok" };
    case "step_failed":
      return { kind: "step bad", detail: event.step + " · " + event.code + " · " + event.message, tone: "bad" };
    case "retry_scheduled":
      return { kind: "repair warn", detail: "attempt " + event.attempt + " scheduled · " + event.message, tone: "warn" };
    case "model_call_started":
      return { kind: "model", detail: event.step + " · " + (event.model || "unknown"), tone: "" };
    case "model_call_completed":
      return {
        kind: "model ok",
        detail: event.step + " · " + (event.model || "unknown") + " · in " + (event.inputTokens ?? "?") +
          " / out " + (event.outputTokens ?? "?") + " tokens · " + ms(event.ms),
        tone: "ok"
      };
    case "run_completed":
      return { kind: "done ok", detail: "run finished in " + ms(event.ms), tone: "ok" };
    case "run_failed":
      return { kind: "done bad", detail: event.code + " · " + event.message, tone: "bad" };
    default:
      return { kind: event.type, detail: "", tone: "" };
  }
}

function appendEvent(event) {
  const parts = describe(event);
  const row = el("li");
  row.append(
    el("span", "time", stamp(event.timestamp)),
    el("span", "kind " + parts.tone, parts.kind),
    el("span", "detail " + parts.tone, parts.detail)
  );
  eventList.append(row);
  eventList.scrollTop = eventList.scrollHeight;
}

function block(title, content) {
  const wrap = el("div", "block");
  wrap.append(el("h3", "", title));
  wrap.append(content);
  return wrap;
}

function codeBlock(source) {
  const pre = el("pre");
  pre.textContent = source;
  return pre;
}

function attemptsList(attempts) {
  if (!attempts || attempts.length === 0) return el("p", "muted", "none");
  const list = el("ol", "attempts");
  for (const attempt of attempts) {
    const item = el("li");
    item.append(el("span", "tag " + (attempt.success ? "ok" : "bad"), attempt.success ? "compiled" : "rejected"));
    item.append(el("span", "", "attempt " + attempt.attempt + (attempt.errors.length ? " · " + attempt.errors.join(" · ") : "")));
    list.append(item);
  }
  return list;
}

function chartFigure(chart) {
  const width = 640;
  const height = 280;
  const pad = { top: 18, right: 14, bottom: 34, left: 56 };
  const innerWidth = width - pad.left - pad.right;
  const innerHeight = height - pad.top - pad.bottom;
  const values = chart.values || [];
  const labels = chart.labels || [];
  const max = Math.max(...values, 1);
  const min = Math.min(...values, 0);
  const span = max - min || 1;

  const svg = document.createElementNS("http://www.w3.org/2000/svg", "svg");
  svg.setAttribute("viewBox", "0 0 " + width + " " + height);
  svg.setAttribute("role", "img");
  svg.setAttribute("aria-label", chart.title);

  const y = (value) => pad.top + innerHeight - ((value - min) / span) * innerHeight;
  const x = (index) => pad.left + (values.length === 1 ? innerWidth / 2 : (index / (values.length - 1)) * innerWidth);

  const axis = document.createElementNS(svg.namespaceURI, "line");
  axis.setAttribute("class", "axis");
  axis.setAttribute("x1", pad.left);
  axis.setAttribute("x2", width - pad.right);
  axis.setAttribute("y1", height - pad.bottom);
  axis.setAttribute("y2", height - pad.bottom);
  svg.append(axis);

  for (const tick of [max, (max + min) / 2, min]) {
    const label = document.createElementNS(svg.namespaceURI, "text");
    label.setAttribute("x", pad.left - 8);
    label.setAttribute("y", y(tick) + 3);
    label.setAttribute("text-anchor", "end");
    label.textContent = Math.round(tick).toLocaleString("en-GB");
    svg.append(label);
  }

  if (chart.kind === "Line") {
    const polyline = document.createElementNS(svg.namespaceURI, "polyline");
    polyline.setAttribute("class", "line");
    polyline.setAttribute("points", values.map((value, index) => x(index) + "," + y(value)).join(" "));
    svg.append(polyline);
    values.forEach((value, index) => {
      const dot = document.createElementNS(svg.namespaceURI, "circle");
      dot.setAttribute("class", "dot");
      dot.setAttribute("cx", x(index));
      dot.setAttribute("cy", y(value));
      dot.setAttribute("r", 2.6);
      svg.append(dot);
    });
  } else {
    const slot = innerWidth / Math.max(values.length, 1);
    values.forEach((value, index) => {
      const bar = document.createElementNS(svg.namespaceURI, "rect");
      bar.setAttribute("class", "bar");
      bar.setAttribute("x", pad.left + index * slot + slot * 0.15);
      bar.setAttribute("width", slot * 0.7);
      bar.setAttribute("y", y(value));
      bar.setAttribute("height", Math.max(height - pad.bottom - y(value), 1));
      svg.append(bar);
    });
  }

  labels.forEach((label, index) => {
    if (labels.length > 8 && index % 2 !== 0) return;
    const text = document.createElementNS(svg.namespaceURI, "text");
    text.setAttribute("x", chart.kind === "Line" ? x(index) : pad.left + (index + 0.5) * (innerWidth / labels.length));
    text.setAttribute("y", height - pad.bottom + 16);
    text.setAttribute("text-anchor", "middle");
    text.textContent = label;
    svg.append(text);
  });

  const figure = el("figure", "chart");
  figure.append(el("figcaption", "", chart.title + " · " + chart.kind.toLowerCase() + " chart"));
  figure.append(svg);
  return figure;
}

function renderResult(data) {
  resultPane.replaceChildren();

  if (data.scenario === "refusal") {
    resultPane.append(el("p", "muted", "The request was refused before any generated code ran."));
    const errors = (data.compileAttempts || []).flatMap((attempt) => attempt.errors);
    const panel = el("div", "rejected", errors.length ? errors.join("\n") : "The compiler refused the script.");
    resultPane.append(block("Policy refusal", panel));
    if (data.plan) resultPane.append(block("Plan", el("p", "", data.plan)));
    if (data.script) {
      const details = el("details");
      details.append(el("summary", "muted", "Rejected script"));
      details.append(codeBlock(data.script));
      resultPane.append(details);
    }
    return;
  }

  if (data.plan) resultPane.append(block("Plan", el("p", "", data.plan)));

  const attempts = attemptsList(data.compileAttempts);
  resultPane.append(block("Compile attempts", attempts));

  if (data.summary) resultPane.append(block("Summary", el("p", "", data.summary)));

  for (const chart of data.charts || []) {
    resultPane.append(block("Chart", chartFigure(chart)));
  }

  if (data.logs && data.logs.length) {
    resultPane.append(block("Sandbox logs", codeBlock(data.logs.join("\n"))));
  }

  if (data.script) {
    const details = el("details");
    details.append(el("summary", "muted", "Script that ran"));
    details.append(codeBlock(data.script));
    resultPane.append(details);
  }
}

function setBusy(busy) {
  analysisButton.disabled = busy;
  refusalButton.disabled = busy;
}

function finish(message, terminal) {
  if (stream) {
    stream.close();
    stream = null;
  }
  setBusy(false);
  statusLine.textContent = message;
  if (!terminal) resultPane.replaceChildren(el("div", "rejected", message));
}

function run(scenario) {
  if (stream) stream.close();
  eventList.replaceChildren();
  resultPane.replaceChildren(el("p", "muted", "running…"));
  sandboxTime.textContent = "";
  statusLine.textContent = "running · " + scenario;
  setBusy(true);

  stream = new EventSource("/api/runs/" + scenario + "/events");
  stream.onmessage = (message) => {
    let event;
    try {
      event = JSON.parse(message.data);
    } catch {
      return;
    }

    if (event.type === "result") {
      renderResult(event);
      sandboxTime.textContent = event.sandboxMs == null ? "" : "sandbox " + ms(event.sandboxMs);
      finish("finished · " + scenario, true);
      return;
    }

    if (event.type === "run_error") {
      const errors = (event.compileAttempts || []).flatMap((attempt) => attempt.errors);
      if (errors.length) {
        resultPane.replaceChildren(
          el("p", "muted", "The run stopped before the sandbox was asked to execute anything."),
          block("Policy refusal", el("div", "rejected", errors.join("\n")))
        );
      }
      finish("stopped", true);
      return;
    }

    appendEvent(event);
  };

  stream.onerror = () => {
    if (stream) finish("stream closed", false);
  };
}

async function loadDoctor() {
  try {
    const response = await fetch("/api/doctor");
    const doctor = await response.json();
    doctorLine.textContent =
      doctor.workerImage + " · limit " + doctor.wallClockSeconds + " s · .NET " + doctor.dotnet;
  } catch {
    doctorLine.textContent = "doctor unavailable";
  }
}

async function loadData() {
  try {
    const response = await fetch("/api/data");
    const text = await response.text();
    const rows = text.trim().split(/\r?\n/).map((line) => line.split(","));
    const [header, ...body] = rows;
    const table = el("table");
    const head = el("tr");
    header.forEach((cell) => head.append(el("th", "", cell)));
    table.append(head);
    body.forEach((cells) => {
      const row = el("tr");
      cells.forEach((cell, index) => row.append(el("td", index === 2 ? "num" : "", cell)));
      table.append(row);
    });
    dataPane.replaceChildren(table);
  } catch {
    dataPane.replaceChildren(el("p", "muted", "dataset unavailable"));
  }
}

analysisButton.addEventListener("click", () => run("analysis"));
refusalButton.addEventListener("click", () => run("refusal"));

loadDoctor();
loadData();