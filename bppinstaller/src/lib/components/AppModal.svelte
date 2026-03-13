<script lang="ts">
  export let open = false;
  export let eyebrow = '';
  export let title = '';
  export let body = '';
  export let confirmText = 'OK';
  export let onConfirm: () => void = () => {};
  export let bodyClass = '';

  function handleConfirm() {
    onConfirm();
  }
</script>

{#if open}
  <div class="modal-backdrop" role="presentation">
    <div
      class="modal-card"
      role="dialog"
      aria-modal="true"
      aria-labelledby="app-modal-title"
    >
      {#if eyebrow}
        <p class="modal-eyebrow">{eyebrow}</p>
      {/if}
      <h2 id="app-modal-title">{title}</h2>
      {#if body}
        <p class={`modal-body ${bodyClass}`.trim()}>{body}</p>
      {:else}
        <div class={`modal-body ${bodyClass}`.trim()}>
          <slot />
        </div>
      {/if}
      <button class="modal-confirm" type="button" onclick={handleConfirm}>
        {confirmText}
      </button>
    </div>
  </div>
{/if}

<style>
  .modal-backdrop {
    position: fixed;
    inset: 0;
    z-index: 40;
    display: grid;
    place-items: center;
    padding: 1rem;
    background: rgba(8, 5, 2, 0.72);
    backdrop-filter: blur(8px);
  }

  .modal-card {
    width: min(100%, 420px);
    padding: 1.4rem 1.25rem 1.2rem;
    background: linear-gradient(180deg, rgba(35, 22, 10, 0.98), rgba(16, 10, 5, 0.96));
    border: 1px solid rgba(200, 148, 55, 0.24);
    border-radius: 4px;
    box-shadow: 0 20px 60px rgba(0, 0, 0, 0.48);
    display: grid;
    gap: 0.85rem;
    text-align: center;
    animation: fade-up 0.2s ease both;
  }

  .modal-card :global(*) {
    box-sizing: border-box;
  }

  .modal-eyebrow {
    margin: 0;
    font-family: 'Cinzel', serif;
    font-size: 0.58rem;
    letter-spacing: 0.28em;
    text-transform: uppercase;
    color: rgba(205, 150, 60, 0.62);
  }

  h2 {
    margin: 0;
    font-family: 'Cinzel Decorative', serif;
    font-size: 1.55rem;
    line-height: 1.1;
    color: #e8c87a;
  }

  .modal-body {
    margin: 0;
    font-size: 0.92rem;
    line-height: 1.6;
    color: rgba(228, 216, 191, 0.8);
    white-space: pre-line;
  }

  .modal-confirm {
    justify-self: center;
    min-width: 150px;
    padding: 0.72rem 1rem;
    font-family: 'Cinzel', serif;
    font-size: 0.68rem;
    letter-spacing: 0.18em;
    text-transform: uppercase;
    color: #1c0e03;
    background: linear-gradient(135deg, #d4a040 0%, #9e5c1e 50%, #d4a040 100%);
    border: 1px solid rgba(210, 158, 60, 0.45);
    border-radius: 2px;
    box-shadow: 0 0 0 1px rgba(255, 198, 98, 0.14) inset, 0 4px 22px rgba(170, 100, 25, 0.3);
    cursor: pointer;
  }

  .modal-confirm:hover {
    transform: translateY(-1px);
    box-shadow: 0 0 0 1px rgba(255, 198, 98, 0.2) inset, 0 6px 30px rgba(170, 100, 25, 0.45);
  }

  .modal-confirm:focus-visible {
    outline: 2px solid rgba(255, 214, 140, 0.9);
    outline-offset: 2px;
  }

  @keyframes fade-up {
    from { opacity: 0; transform: translateY(14px); }
    to   { opacity: 1; transform: translateY(0); }
  }
</style>
