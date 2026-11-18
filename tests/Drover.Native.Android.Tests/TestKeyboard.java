package org.drover.tests.keyboard;

import android.content.BroadcastReceiver;
import android.content.Context;
import android.content.Intent;
import android.content.IntentFilter;
import android.inputmethodservice.InputMethodService;

public final class TestKeyboard extends InputMethodService {
    public static final String EDIT = "org.drover.tests.keyboard.EDIT";

    private final BroadcastReceiver edits = new BroadcastReceiver() {
        @Override
        public void onReceive(Context context, Intent intent) {
            var connection = getCurrentInputConnection();
            if (connection == null) {
                setResultCode(1);
                return;
            }

            connection.beginBatchEdit();
            if (intent.hasExtra("delete")) {
                connection.deleteSurroundingText(intent.getIntExtra("delete", 1), 0);
            } else if (intent.hasExtra("compose")) {
                connection.setComposingText(intent.getStringExtra("compose"), 1);
            } else if (intent.hasExtra("finish")) {
                connection.finishComposingText();
            } else if (intent.hasExtra("select")) {
                var position = intent.getIntExtra("select", 0);
                connection.setSelection(position, position);
            } else {
                connection.commitText(intent.getStringExtra("text"), 1);
            }
            connection.endBatchEdit();
            setResultCode(2);
        }
    };

    @Override
    public void onCreate() {
        super.onCreate();
        registerReceiver(edits, new IntentFilter(EDIT), "android.permission.DUMP", null, Context.RECEIVER_EXPORTED);
    }

    @Override
    public void onDestroy() {
        unregisterReceiver(edits);
        super.onDestroy();
    }
}
