export const mapHoldDelay = 300;

export function installMapLongPress(canvas: HTMLElement, onHold: (point: [number, number]) => void,
  delay = mapHoldDelay, tolerance = 10) {
  const capture = { capture: true };
  let timer: ReturnType<typeof setTimeout> | undefined;
  let pointer: number | undefined;
  let start: [number, number] = [0, 0];
  let held = false;
  let suppressUntil = 0;
  function cancel() {
    if (held) suppressUntil = performance.now() + 800;
    clearTimeout(timer); timer = undefined; pointer = undefined;
  }
  function down(event: PointerEvent) {
    cancel();
    if (!event.isPrimary || event.button !== 0) return;
    held = false; suppressUntil = 0;
    pointer = event.pointerId;
    start = [event.clientX, event.clientY];
    timer = setTimeout(() => {
      timer = undefined;
      held = true;
      const bounds = canvas.getBoundingClientRect();
      onHold([start[0] - bounds.left, start[1] - bounds.top]);
    }, delay);
  }
  function move(event: PointerEvent) {
    if (pointer === event.pointerId && Math.hypot(event.clientX - start[0], event.clientY - start[1]) > tolerance) cancel();
  }
  function up(event: PointerEvent) {
    if (pointer !== event.pointerId) return;
    if (held) suppressUntil = performance.now() + 800;
    cancel();
  }
  function suppressClick() { return held && (pointer !== undefined || performance.now() < suppressUntil); }
  function click(event: MouseEvent) {
    if (suppressClick()) { event.preventDefault(); event.stopImmediatePropagation(); }
  }
  canvas.addEventListener('pointerdown', down);
  canvas.addEventListener('pointermove', move);
  canvas.addEventListener('pointerup', up);
  canvas.addEventListener('pointercancel', cancel);
  canvas.addEventListener('pointerleave', cancel);
  canvas.addEventListener('click', click, capture);
  return { cancel, suppressClick, dispose() {
    cancel();
    canvas.removeEventListener('pointerdown', down);
    canvas.removeEventListener('pointermove', move);
    canvas.removeEventListener('pointerup', up);
    canvas.removeEventListener('pointercancel', cancel);
    canvas.removeEventListener('pointerleave', cancel);
    canvas.removeEventListener('click', click, capture);
  } };
}
