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
