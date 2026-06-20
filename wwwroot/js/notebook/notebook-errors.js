// SECTION: Typed notebook reconciliation errors
export class NotebookCardHtmlError extends Error {
  constructor(message) {
    super(message);
    this.name = 'NotebookCardHtmlError';
    this.code = 'notebook_invalid_card_html';
  }
}

export class NotebookBoardTargetError extends Error {
  constructor(message) {
    super(message);
    this.name = 'NotebookBoardTargetError';
    this.code = 'notebook_target_board_missing';
  }
}

export class NotebookBoardUpdateError extends Error {
  constructor(message) {
    super(message);
    this.name = 'NotebookBoardUpdateError';
    this.code = 'notebook_board_update_failed';
  }
}

// SECTION: Notebook validation error extraction helpers
export function getValidationMessages(error) {
  const errors = error?.errors;

  if (!errors || typeof errors !== 'object') return [];

  return Object.entries(errors).flatMap(([field, value]) => {
    const messages = Array.isArray(value) ? value : [value];

    return messages
      .filter((message) => typeof message === 'string' && message.trim().length > 0)
      .map((message) => ({ field, message: message.trim() }));
  });
}

export function getFirstValidationMessage(error) {
  const messages = getValidationMessages(error);

  if (messages.length > 0) return messages[0].message;

  return error?.message || 'The note contains invalid information.';
}
