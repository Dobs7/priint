# Priint

**Download e uso gratuitos.** Qualquer pessoa pode baixar, instalar e usar este aplicativo sem pagar ao Dobs by Azura. Serviços externos, assinaturas e conteúdos de terceiros mantêm suas próprias condições.

[Baixar para Windows](https://github.com/Dobs7/priint/releases/download/v6.0.1/Priint-Instalador-6.0.1.exe) · [Ver a release e os arquivos](https://github.com/Dobs7/priint/releases/tag/v6.0.1)

## Como foi feito
Desenvolvido em C# com .NET Framework e Windows Forms. Combina captura de tela, FFmpeg/FFprobe, NAudio, SharpDX e OpenCV/OpenCvSharp para gravação, áudio e câmera opcional.

Captura de tela e gravação para Windows, por **Dobs by Azura**. Permite fazer prints, anotar imagens e gravar monitor, janela ou região, com opções de áudio, câmera e codificação.

## Baixar e começar
Baixe `Priint-Instalador-6.0.1.exe` em **Releases** e instale. O novo pacote inclui FFmpeg e FFprobe; não depende de uma instalação prévia desses motores.

- Abra Priint Studio pelo menu Iniciar para configurar uma gravação.
- Para print, use a bandeja do sistema ou Ctrl+Shift+PrintScreen.
- Ctrl+Shift+F9 abre o Studio/inicia ou para; Ctrl+Shift+F8 pausa/retoma; Ctrl+Shift+F10 para e salva. Os atalhos podem ser ajustados.
- Escolha fonte, destino, resolução, FPS, qualidade e codificação. Ative áudio e câmera apenas quando quiser usá-los.
- A conclusão informa o arquivo gerado; confira-o antes de compartilhar.

As opções incluem CPU e NVIDIA NVENC quando disponível, formatos de vídeo e imagem, áudio do computador/microfone e câmera opcional com enquadramento. Resolução, FPS e fluidez dependem de hardware, driver, fonte e carga. Janela visível pode incluir sobreposições; teste sua configuração antes de uma gravação importante.

## Privacidade
Pacote sem prints, vídeos, câmeras escolhidas, dispositivos, preferências ou arquivos pessoais do desenvolvedor. Câmera e microfone não vêm ativados no perfil inicial. Preferências e arquivos criados por você ficam no seu Windows. O tratamento de imagem e o rastreio de rosto são locais. A desinstalação preserva seus prints, vídeos e preferências.

## Desenvolvimento
Windows, .NET Framework e compilador C# do Windows. Restaure os pacotes NuGet nas versões de DEPENDENCIAS.json em deps/, execute build.ps1 e build-installer.ps1. O segundo script usa assets/tools.zip com FFmpeg/FFprobe e a licença correspondente. Os binários são distribuídos na release, não no Git. Build sem certificado de assinatura de código.

## Créditos
Contribuições próprias por Dobs by Azura. NAudio, OpenCvSharp/OpenCV, SharpDX e bibliotecas de suporte: licenças em assets/THIRD-PARTY.txt. FFmpeg/FFprobe: https://ffmpeg.org, build 9.0.2-essentials_build-www.gyan.dev, GPL conforme tools/FFMPEG-LICENSE.txt. Preserve créditos, licenças e fontes correspondentes ao redistribuir. O código próprio não recebeu nova licença aberta nesta preparação.
