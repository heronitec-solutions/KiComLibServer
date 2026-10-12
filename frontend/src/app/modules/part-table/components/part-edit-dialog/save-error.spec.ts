import { describeSaveFailure } from './save-error';

describe('describeSaveFailure', () => {
  it('names each invalid field and why it failed', () => {
    const failure = describeSaveFailure({
      error: {
        title: 'One or more validation errors occurred.',
        errors: {
          Name: ['The Name field is required.'],
          Value: ["The field Value must be a string or array type with a maximum length of '100'."]
        }
      }
    });

    expect(failure.summary).toBe('Saving failed. Check the values and try again.');
    expect(failure.details).toEqual([
      'Name: The Name field is required.',
      "Value: The field Value must be a string or array type with a maximum length of '100'."
    ]);
  });

  it('reads a JSON string body and labels catalog fields', () => {
    const failure = describeSaveFailure({
      error: JSON.stringify({
        errors: {
          name: ['This name is already used by another part.'],
          symbolId: ['The selected symbol does not exist.']
        }
      })
    });

    expect(failure.details).toEqual([
      'Name: This name is already used by another part.',
      'Symbol: The selected symbol does not exist.'
    ]);
  });

  it('keeps only the summary when the response has no field details', () => {
    expect(describeSaveFailure({ status: 500 }).details).toEqual([]);
    expect(describeSaveFailure({ error: '<html>Exception</html>' }).details).toEqual([]);
  });
});
