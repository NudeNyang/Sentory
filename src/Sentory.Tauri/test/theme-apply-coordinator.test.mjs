import assert from "node:assert/strict";
import test from "node:test";
import { createThemeApplyCoordinator } from "../web/theme-apply-coordinator.js";

function createFakeScheduler() {
  let nextId = 1;
  const jobs = new Map();
  return {
    schedule(callback) {
      const id = nextId++;
      jobs.set(id, callback);
      return id;
    },
    cancel(id) {
      jobs.delete(id);
    },
    flush() {
      const pending = [...jobs.values()];
      jobs.clear();
      pending.forEach(callback => callback());
    },
  };
}

test("rapid theme changes apply only the final native title bar theme", () => {
  const scheduler = createFakeScheduler();
  const applied = [];
  const coordinator = createThemeApplyCoordinator({
    applyTheme: dark => { applied.push(dark); },
    schedule: scheduler.schedule,
    cancel: scheduler.cancel,
    settleDelay: 180,
  });

  coordinator.request(false);
  coordinator.request(true);
  coordinator.request(false);
  coordinator.request(true);

  assert.deepEqual(applied, [false]);
  scheduler.flush();
  assert.deepEqual(applied, [false, true]);
});

test("returning to the applied theme during a rapid change cancels the repaint", () => {
  const scheduler = createFakeScheduler();
  const applied = [];
  const coordinator = createThemeApplyCoordinator({
    applyTheme: dark => { applied.push(dark); },
    schedule: scheduler.schedule,
    cancel: scheduler.cancel,
  });

  coordinator.request(false);
  coordinator.request(true);
  coordinator.request(false);
  scheduler.flush();

  assert.deepEqual(applied, [false]);
});
