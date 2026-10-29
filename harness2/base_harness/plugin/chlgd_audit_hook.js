// [c8] Tool-loop middleware hook for CHLGD & CEO Rule Auditing
export default function chlgdAuditHook(ctx) {
  return { onBeforeTool: true, onAfterTool: true, surface: 'jarvis-harness2' };
}
