"""Independent Zheli windows, reusing the upstream chat widgets and services."""
from PySide6.QtCore import Qt, QTimer, QEvent, QObject
from PySide6.QtWidgets import (QWidget, QVBoxLayout, QHBoxLayout, QPushButton,
                             QLabel, QFrame, QApplication, QGraphicsDropShadowEffect)
from PySide6.QtGui import QColor


class AssistantWindow(QWidget):
    def __init__(self, pet):
        super().__init__()
        self.pet = pet
        self.mode = 'pet'
        self.setWindowTitle('哲喵')
        self.setMinimumSize(360, 300)
        self.setStyleSheet('''
            QWidget { background:#FFF9F0; color:#49372F; font-family:"PingFang SC"; font-size:14px; }
            QPushButton { background:#DCEEFF; border:1px solid #BCD8EF; border-radius:12px; padding:9px 14px; }
            QPushButton:hover { background:#C4E2FB; }
            QPushButton:pressed { background:#ABCFEA; }
            QLabel#brand { font-size:22px; font-weight:600; }
            QFrame#navigation { background:#F3E8D9; border-radius:18px; }
        ''')
        root = QVBoxLayout(self)
        root.setContentsMargins(18, 16, 18, 18)
        top = QHBoxLayout()
        title = QLabel('哲喵 · 陪你把事情做好')
        title.setObjectName('brand')
        top.addWidget(title, 1)
        self.expand = QPushButton('展开完整窗口')
        self.expand.clicked.connect(lambda: self.present('full' if self.mode == 'quick' else 'quick'))
        top.addWidget(self.expand)
        hide = QPushButton('收起')
        hide.clicked.connect(self.close)
        top.addWidget(hide)
        root.addLayout(top)
        body = QHBoxLayout()
        self.navigation = QFrame()
        self.navigation.setObjectName('navigation')
        self.navigation.setFixedWidth(160)
        nav = QVBoxLayout(self.navigation)
        nav.addWidget(QLabel('我的小助手'))
        for caption, callback in [('对话', lambda: pet.chat_input.setFocus()),
                                  ('添加文件', pet._pick_attach_files),
                                  ('记忆', pet._open_memory_manager),
                                  ('导出对话', pet._export_chat),
                                  ('设置', pet._open_settings)]:
            button = QPushButton(caption)
            button.clicked.connect(lambda checked=False, fn=callback: fn())
            nav.addWidget(button)
        nav.addStretch()
        nav.addWidget(QLabel('文件会作为附件\n交给哲喵处理'))
        body.addWidget(self.navigation)
        pet.layout.removeWidget(pet.chat_panel)
        pet.chat_panel.setParent(self)
        pet.chat_panel.setMinimumSize(0, 0)
        pet.chat_panel.setMaximumSize(16777215, 16777215)
        # Upstream edge handlers resize the pet window; native window resizing owns this now.
        pet.chat_panel.mousePressEvent = lambda event: QFrame.mousePressEvent(pet.chat_panel, event)
        pet.chat_panel.mouseMoveEvent = lambda event: QFrame.mouseMoveEvent(pet.chat_panel, event)
        pet.chat_panel.mouseReleaseEvent = lambda event: QFrame.mouseReleaseEvent(pet.chat_panel, event)
        body.addWidget(pet.chat_panel, 1)
        root.addLayout(body, 1)
        footer = QHBoxLayout()
        footer.addWidget(QLabel('Enter 发送 · Shift+Enter 换行'), 1)
        send = QPushButton('发送  ↗')
        send.clicked.connect(pet._on_chat_input)
        footer.addWidget(send)
        root.addLayout(footer)
        effect = QGraphicsDropShadowEffect(pet.chat_panel)
        effect.setBlurRadius(18)
        effect.setOffset(0, 3)
        effect.setColor(QColor(111, 81, 57, 35))
        pet.chat_panel.setGraphicsEffect(effect)

    def present(self, mode):
        if mode not in ('quick', 'full'):
            raise ValueError(mode)
        was_visible = self.isVisible()
        self.mode = mode
        full = mode == 'full'
        self.navigation.setVisible(full)
        self.pet.side_panel.setVisible(full)
        self.expand.setText('切换快捷对话' if full else '展开完整窗口')
        self.resize(1100 if full else 480, 680 if full else 360)
        area = (self.pet.screen() or QApplication.primaryScreen()).availableGeometry()
        self.resize(min(self.width(), area.width()), min(self.height(), area.height()))
        if not was_visible:
            anchor = self.pet.frameGeometry()
            self.move(anchor.left() - self.width() + anchor.width(), anchor.bottom() + 8)
        self.move(max(area.left(), min(self.x(), area.right() - self.width() + 1)),
                  max(area.top(), min(self.y(), area.bottom() - self.height() + 1)))
        self.pet.chat_panel.show()
        self.show()
        self.raise_()
        self.activateWindow()
        self.pet.chat_input.setFocus()

    def closeEvent(self, event):
        self.mode = 'pet'
        self.hide()
        event.ignore()


class PetClickFilter(QObject):
    """Single click opens quick chat; dragging keeps the upstream pet movement."""
    def __init__(self, pet, shell):
        super().__init__(pet)
        self.pet, self.shell, self.press = pet, shell, None

    def eventFilter(self, obj, event):
        if event.type() == QEvent.MouseButtonPress and event.button() == Qt.LeftButton:
            self.press = event.globalPosition().toPoint()
        elif event.type() == QEvent.MouseButtonRelease and event.button() == Qt.LeftButton:
            if self.press is not None and (event.globalPosition().toPoint() - self.press).manhattanLength() < 6:
                QTimer.singleShot(0, lambda: self.shell.present('quick'))
            self.press = None
        return False


def install(pet):
    shell = AssistantWindow(pet)
    pet._zheli_shell = shell  # retain the top-level window for the pet lifetime
    pet.toggle_chat_panel = lambda: shell.close() if shell.isVisible() else shell.present('quick')
    pet._sync_window_to_panel = lambda: None
    pet.setFixedSize(440, 340)
    click_filter = PetClickFilter(pet, shell)
    pet._zheli_click_filter = click_filter
    pet.installEventFilter(click_filter)
    return shell
